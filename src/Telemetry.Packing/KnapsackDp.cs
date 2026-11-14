using Telemetry.Core;

namespace Telemetry.Packing;

/// <summary>
/// Рюкзак 0/1 динамічним програмуванням, O(n·W), з відновленням розв'язку.
/// Ваги — довжини 12 байтів кадру за ЄДИНОЮ таблицею Гаффмана вікна (64 кадри);
/// W = 8·(max_payload − 14 − 2·table_nsym), CLASS_TABLE = 0. Власник: Георгій.
/// </summary>
/// <remarks>
/// <para>
/// best[c] — найбільша цінність при сумарній вазі ≤ c після розгляду перших i предметів; проходимо c
/// згори вниз, щоб кожен предмет брався не більше одного разу. take[i, c] запам'ятовує, чи предмет i
/// покращив best[c]; відновлення йде від c = W назад по предметах. Пам'ять: O(W) для цінностей і
/// n·(W+1) біт для відновлення (64 × 1304 ≈ 83 000 біт на пакет).
/// </para>
/// <para>
/// Предмети з від'ємною вагою, вагою більшою за W або нульовою/від'ємною цінністю не беруться (C2 п.4:
/// кадр більший за пакет просто не обирається, без зациклення). Предмет нульової ваги з додатною
/// цінністю береться завжди. Рівні оптимуми розв'язуються на користь меншої сумарної ваги, далі —
/// детерміновано (порядок предметів).
/// </para>
/// </remarks>
public sealed class KnapsackDp : IPacker
{
    /// <inheritdoc/>
    /// <exception cref="ArgumentException">Різні довжини масивів.</exception>
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
            return [.. Enumerable.Range(0, weightsBits.Length).Where(i => weightsBits[i] == 0 && values[i] > 0)];
        }

        var n = weightsBits.Length;
        var best = new long[capacityBits + 1];
        var take = new global::System.Collections.BitArray[n];
        for (var i = 0; i < n; i++)
        {
            take[i] = new global::System.Collections.BitArray(capacityBits + 1);
            var w = weightsBits[i];
            var v = values[i];
            if (w < 0 || w > capacityBits || v <= 0)
            {
                continue;
            }

            for (var c = capacityBits; c >= w; c--)
            {
                var candidate = best[c - w] + v;
                if (candidate > best[c])
                {
                    best[c] = candidate;
                    take[i][c] = true;
                }
            }
        }

        // Найменша місткість, на якій досягається оптимум, — щоб серед рівних розв'язків узяти легший.
        var cap = capacityBits;
        while (cap > 0 && best[cap - 1] == best[cap])
        {
            cap--;
        }

        var chosen = new List<int>();
        for (var i = n - 1; i >= 0; i--)
        {
            if (take[i][cap])
            {
                chosen.Add(i);
                cap -= weightsBits[i];
            }
        }

        chosen.Reverse();
        return [.. chosen];
    }

    /// <summary>Сумарна цінність обраних предметів.</summary>
    public static long TotalValue(int[] selected, int[] values)
    {
        ArgumentNullException.ThrowIfNull(selected);
        ArgumentNullException.ThrowIfNull(values);
        return selected.Sum(i => (long)values[i]);
    }

    /// <summary>Сумарна вага обраних предметів.</summary>
    public static long TotalWeight(int[] selected, int[] weightsBits)
    {
        ArgumentNullException.ThrowIfNull(selected);
        ArgumentNullException.ThrowIfNull(weightsBits);
        return selected.Sum(i => (long)weightsBits[i]);
    }
}
