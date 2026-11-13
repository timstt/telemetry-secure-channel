using System.Diagnostics.CodeAnalysis;

namespace Telemetry.Huffman;

/// <summary>
/// Запис бітів MSB-first: перший біт стає бітом 0x80 першого байта;
/// останній байт добивається нулями (FORMAT_SPEC §0 п.3–4). Власник: Саша.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Заглушка каркаса; прибрати атрибут разом з реалізацією")]
public sealed class BitWriter
{
    /// <summary>Кількість записаних бітів.</summary>
    public int BitCount => throw new NotImplementedException();

    /// <summary>Записує <paramref name="length"/> молодших бітів <paramref name="code"/>, старший першим.</summary>
    public void Write(uint code, int length) => throw new NotImplementedException();

    /// <summary>Повертає байти потоку (останній байт доповнено нулями).</summary>
    public byte[] ToArray() => throw new NotImplementedException();
}
