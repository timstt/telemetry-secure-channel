namespace Telemetry.Huffman;

/// <summary>
/// Відмова від стиснення (прапорець NO_COMPRESSION = 0x08):
/// table_nsym = 0, bitstream_len = symbol_count, сирі байти.
/// </summary>
public static class NoCompression
{
    /// <summary>Ентропія Шеннона байтів, біт/символ.</summary>
    public static double Entropy(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
        {
            return 0;
        }

        Span<int> counts = stackalloc int[256];
        foreach (var b in data)
        {
            counts[b]++;
        }

        var entropy = 0.0;
        foreach (var c in counts)
        {
            if (c == 0)
            {
                continue;
            }

            var p = (double)c / data.Length;
            entropy -= p * Math.Log2(p);
        }

        return entropy;
    }

    /// <summary>Чи вигідніше писати сирі байти: ентропія не нижча за поріг.</summary>
    public static bool ShouldSkipCompression(ReadOnlySpan<byte> data, double entropyThreshold) =>
        !data.IsEmpty && Entropy(data) >= entropyThreshold;
}
