namespace Telemetry.Huffman;

/// <summary>
/// Запис бітів MSB-first: перший біт стає бітом 0x80 першого байта,
/// останній байт добивається нулями (FORMAT_SPEC §0 п.3–4).
/// </summary>
public sealed class BitWriter
{
    private byte[] _buffer = new byte[16];

    /// <summary>Кількість записаних бітів.</summary>
    public int BitCount { get; private set; }

    /// <summary>Записує <paramref name="length"/> молодших бітів <paramref name="code"/>, старший першим.</summary>
    public void Write(uint code, int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(length, 32);

        for (var i = length - 1; i >= 0; i--)
        {
            var byteIndex = BitCount >> 3;
            if (byteIndex == _buffer.Length)
            {
                Array.Resize(ref _buffer, _buffer.Length * 2);
            }

            if (((code >> i) & 1u) != 0)
            {
                _buffer[byteIndex] |= (byte)(0x80 >> (BitCount & 7));
            }

            BitCount++;
        }
    }

    /// <summary>Повертає байти потоку (останній байт доповнено нулями).</summary>
    public byte[] ToArray()
    {
        var result = new byte[(BitCount + 7) >> 3];
        Array.Copy(_buffer, result, result.Length);
        return result;
    }
}
