using Telemetry.Core;

namespace Telemetry.Huffman;

/// <summary>Жадібний рюкзак: предмети за щільністю v/w за спаданням, беремо те, що влазить.</summary>
public sealed class KnapsackGreedy : IPacker
{
    /// <inheritdoc/>
    public int[] Select(int[] weightsBits, int[] values, int capacityBits)
    {
        ArgumentNullException.ThrowIfNull(weightsBits);
        ArgumentNullException.ThrowIfNull(values);
        if (weightsBits.Length != values.Length)
        {
            throw new ArgumentException("Довжини ваг і цінностей різні.");
        }

        if (capacityBits <= 0)
        {
            return [];
        }

        var order = Enumerable.Range(0, weightsBits.Length).ToArray();
        Array.Sort(order, (a, b) => Compare(a, b, weightsBits, values));

        var chosen = new List<int>();
        long used = 0;
        foreach (var i in order)
        {
            if (weightsBits[i] < 0 || values[i] <= 0 || used + weightsBits[i] > capacityBits)
            {
                continue;
            }

            used += weightsBits[i];
            chosen.Add(i);
        }

        chosen.Sort();
        return [.. chosen];
    }

    private static int Compare(int a, int b, int[] w, int[] v)
    {
        // v_a/w_a проти v_b/w_b без ділення: v_a·w_b проти v_b·w_a
        var byDensity = ((long)v[b] * w[a]).CompareTo((long)v[a] * w[b]);
        if (byDensity != 0)
        {
            return byDensity;
        }

        var byValue = v[b].CompareTo(v[a]);
        return byValue != 0 ? byValue : a.CompareTo(b);
    }
}
