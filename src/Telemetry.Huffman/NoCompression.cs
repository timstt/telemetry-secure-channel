using System.Diagnostics.CodeAnalysis;

namespace Telemetry.Huffman;

/// <summary>
/// Свідома відмова від стиснення (C2 п.5, прапорець NO_COMPRESSION = 0x08):
/// table_nsym = 0, bitstream_len = symbol_count, сирі байти. Власник: Саша.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Заглушка каркаса; прибрати атрибут разом з реалізацією")]
public static class NoCompression
{
    /// <summary>Ентропія Шеннона байтів, біт/символ.</summary>
    public static double Entropy(ReadOnlySpan<byte> data) => throw new NotImplementedException();

    /// <summary>Чи вигідніше писати сирі байти (поріг ентропії — ген ГА, пастка 19).</summary>
    public static bool ShouldSkipCompression(ReadOnlySpan<byte> data, double entropyThreshold) =>
        throw new NotImplementedException();
}
