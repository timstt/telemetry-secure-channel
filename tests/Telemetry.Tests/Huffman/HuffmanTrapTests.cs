using Telemetry.Huffman;
using Telemetry.Tests.Infrastructure;

namespace Telemetry.Tests.Huffman;

/// <summary>Пастки 1–4 (зона Саші). Прибрати Skip, коли зона реалізована.</summary>
public class HuffmanTrapTests
{
    private static readonly byte[] Ref01Frame = TestData.Hex("00000000 03E8 00D7 FFFD 01 01");

    [Fact(Skip = TestData.Pending)]
    public void Trap01_bits_msb_first_and_zero_padding()
    {
        var w = new BitWriter();
        w.Write(0b1, 1);
        w.Write(0b01, 2);
        Assert.Equal(3, w.BitCount);
        Assert.Equal(new byte[] { 0b1010_0000 }, w.ToArray());
    }

    [Fact(Skip = TestData.Pending)]
    public void Trap02_ref01_lengths_codes_and_bitstream()
    {
        var codec = new HuffmanCodec();
        var freq = new long[256];
        foreach (var b in Ref01Frame)
        {
            freq[b]++;
        }

        var lengths = codec.BuildLengths(freq);
        Assert.Equal(1, lengths[0x00]);
        Assert.Equal(3, lengths[0x01]);
        Assert.Equal(3, lengths[0xFF]);
        Assert.All(new[] { 0x03, 0xD7, 0xE8, 0xFD }, s => Assert.Equal(4, lengths[s]));

        var codes = HuffmanCodec.CanonicalCodes(lengths);
        Assert.Equal(0b100u, codes[0x01]); // при рівній довжині менший символ отримує менший код
        Assert.Equal(0b101u, codes[0xFF]);

        var (data, bits) = codec.Encode(Ref01Frame, lengths);
        Assert.Equal(31, bits);
        Assert.Equal(TestData.Hex("0CE6DF90"), data);
        Assert.Equal(Ref01Frame, codec.Decode(data, lengths, Ref01Frame.Length));
    }

    [Fact(Skip = TestData.Pending)]
    public void Trap03_code_length_limited_to_15_without_merging_symbols()
    {
        // Частоти Фібоначчі дають незбалансоване дерево глибиною > 15.
        var freq = new long[256];
        long a = 1, b = 1;
        for (var s = 0; s < 30; s++)
        {
            freq[s] = a;
            (a, b) = (b, a + b);
        }

        var lengths = new HuffmanCodec().BuildLengths(freq);
        for (var s = 0; s < 30; s++)
        {
            Assert.InRange(lengths[s], 1, HuffmanCodec.MaxCodeLength); // жоден символ не злито й не викинуто
        }

        var kraft = lengths.Where(l => l > 0).Sum(l => Math.Pow(2, -l));
        Assert.True(kraft <= 1.0 + 1e-12);
    }

    [Fact(Skip = TestData.Pending)]
    public void Trap03_degenerate_alphabet_gets_length_1_code_0()
    {
        var freq = new long[256];
        freq[0] = 12;
        var lengths = new HuffmanCodec().BuildLengths(freq);
        Assert.Equal(1, lengths[0]);
        Assert.Equal(1, lengths.Count(l => l > 0));
        Assert.Equal(0u, HuffmanCodec.CanonicalCodes(lengths)[0]);
    }

    [Fact(Skip = TestData.Pending)]
    public void Trap04_no_compression_for_uniform_bytes()
    {
        var data = Enumerable.Range(0, 256).Select(i => (byte)i).ToArray();
        Assert.Equal(8.0, NoCompression.Entropy(data), 6);
        Assert.True(NoCompression.ShouldSkipCompression(data, entropyThreshold: 7.5));
    }
}
