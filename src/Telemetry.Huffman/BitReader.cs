using System.Diagnostics.CodeAnalysis;

namespace Telemetry.Huffman;

/// <summary>Читання бітів MSB-first. Обрив потоку → E_CODE на рівні декодера. Власник: Саша.</summary>
[ExcludeFromCodeCoverage(Justification = "Заглушка каркаса; прибрати атрибут разом з реалізацією")]
public sealed class BitReader
{
    /// <summary>Створює читач над байтами потоку.</summary>
    public BitReader(byte[] data) => throw new NotImplementedException();

    /// <summary>Читає один біт; false, якщо потік скінчився.</summary>
    public bool TryReadBit(out int bit) => throw new NotImplementedException();
}
