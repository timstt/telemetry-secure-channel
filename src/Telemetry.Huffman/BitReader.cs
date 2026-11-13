namespace Telemetry.Huffman;

/// <summary>Читання бітів MSB-first.</summary>
public sealed class BitReader
{
    private readonly byte[] _data;
    private int _position;

    /// <summary>Створює читач над байтами потоку.</summary>
    public BitReader(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        _data = data;
    }

    /// <summary>Кількість бітів, що лишилися.</summary>
    public int Remaining => (_data.Length << 3) - _position;

    /// <summary>Читає один біт; false, якщо потік скінчився.</summary>
    public bool TryReadBit(out int bit)
    {
        if (_position >= _data.Length << 3)
        {
            bit = 0;
            return false;
        }

        bit = (_data[_position >> 3] >> (7 - (_position & 7))) & 1;
        _position++;
        return true;
    }
}
