using Telemetry.Huffman;

namespace Telemetry.Tests.Huffman;

public class BitIoTests
{
    [Fact]
    public void Writer_packs_msb_first_across_bytes_and_pads_with_zeros()
    {
        var w = new BitWriter();
        w.Write(0b101, 3);
        w.Write(0b1111_1111_1, 9);
        Assert.Equal(12, w.BitCount);
        Assert.Equal(new byte[] { 0b1011_1111, 0b1111_0000 }, w.ToArray());
    }

    [Fact]
    public void Writer_empty_and_zero_length_writes()
    {
        var w = new BitWriter();
        w.Write(0xFF, 0);
        Assert.Equal(0, w.BitCount);
        Assert.Empty(w.ToArray());
    }

    [Fact]
    public void Writer_grows_and_writes_32_bit_values()
    {
        var w = new BitWriter();
        for (var i = 0; i < 20; i++)
        {
            w.Write(0xDEADBEEF, 32);
        }

        var bytes = w.ToArray();
        Assert.Equal(80, bytes.Length);
        Assert.Equal(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF }, bytes[..4]);
    }

    [Fact]
    public void Writer_rejects_bad_length()
    {
        var w = new BitWriter();
        Assert.Throws<ArgumentOutOfRangeException>(() => w.Write(1, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => w.Write(1, 33));
    }

    [Fact]
    public void Reader_returns_bits_in_order_and_reports_end()
    {
        var r = new BitReader([0b1010_0000]);
        var bits = new List<int>();
        while (r.TryReadBit(out var bit))
        {
            bits.Add(bit);
        }

        Assert.Equal(new[] { 1, 0, 1, 0, 0, 0, 0, 0 }, bits);
        Assert.Equal(0, r.Remaining);
        Assert.False(r.TryReadBit(out var last));
        Assert.Equal(0, last);
    }

    [Fact]
    public void Writer_and_reader_roundtrip()
    {
        var rnd = new Random(1);
        var w = new BitWriter();
        var expected = new List<int>();
        for (var i = 0; i < 500; i++)
        {
            var len = rnd.Next(1, 16);
            var code = (uint)rnd.Next(1 << len);
            w.Write(code, len);
            for (var k = len - 1; k >= 0; k--)
            {
                expected.Add((int)((code >> k) & 1));
            }
        }

        var r = new BitReader(w.ToArray());
        foreach (var e in expected)
        {
            Assert.True(r.TryReadBit(out var bit));
            Assert.Equal(e, bit);
        }
    }
}
