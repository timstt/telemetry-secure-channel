using Telemetry.Huffman;

namespace Telemetry.Tests.Huffman;

public class HuffmanCodecTests
{
    public static TheoryData<string> CompressedRefNames => new()
    {
        "ref_01_single_frame",
        "ref_03_degenerate",
        "ref_04_session",
        "ref_06_class_table",
    };

    [Theory]
    [MemberData(nameof(CompressedRefNames))]
    public void Reference_codes_match_canonical_algorithm(string name)
    {
        var reference = ReferenceFile.Load(name);
        var codes = HuffmanCodec.CanonicalCodes(reference.Lengths());
        foreach (var (symbol, length, code) in reference.Table)
        {
            Assert.Equal(code, Convert.ToString(codes[symbol], 2).PadLeft(length, '0'));
        }
    }

    [Theory]
    [MemberData(nameof(CompressedRefNames))]
    public void Reference_bitstream_decodes_to_frames(string name)
    {
        var reference = ReferenceFile.Load(name);
        var decoded = new HuffmanCodec().Decode(reference.Bitstream, reference.Lengths(), 12 * reference.FrameCount);
        Assert.Equal(reference.FrameBytes, decoded);
    }

    [Theory]
    [MemberData(nameof(CompressedRefNames))]
    public void Reference_lengths_and_bitstream_are_reproduced(string name)
    {
        var reference = ReferenceFile.Load(name);
        var codec = new HuffmanCodec();
        var freq = new long[256];
        foreach (var b in reference.FrameBytes)
        {
            freq[b]++;
        }

        var lengths = codec.BuildLengths(freq);
        Assert.Equal(reference.Lengths(), lengths);
        Assert.Equal(reference.Bitstream, codec.Encode(reference.FrameBytes, lengths).Data);
    }

    [Fact]
    public void Empty_input_gives_empty_table_and_stream()
    {
        var codec = new HuffmanCodec();
        var lengths = codec.BuildLengths(new long[256]);
        Assert.All(lengths, l => Assert.Equal(0, l));
        var (data, bits) = codec.Encode([], lengths);
        Assert.Empty(data);
        Assert.Equal(0, bits);
        Assert.Empty(codec.Decode([], lengths, 0));
    }

    [Fact]
    public void Degenerate_alphabet_roundtrip()
    {
        var codec = new HuffmanCodec();
        byte[] src = [7, 7, 7, 7, 7, 7, 7, 7, 7];
        var freq = new long[256];
        freq[7] = src.Length;
        var lengths = codec.BuildLengths(freq);
        var (data, bits) = codec.Encode(src, lengths);
        Assert.Equal(9, bits);
        Assert.Equal(new byte[] { 0, 0 }, data);
        Assert.Equal(src, codec.Decode(data, lengths, src.Length));
    }

    [Fact]
    public void Random_data_roundtrip_with_kraft_and_limit()
    {
        var rnd = new Random(12345);
        var codec = new HuffmanCodec();
        for (var round = 0; round < 200; round++)
        {
            var alphabet = rnd.Next(1, 257);
            var src = new byte[rnd.Next(1, 600)];
            for (var i = 0; i < src.Length; i++)
            {
                // перекошений розподіл, щоб з'являлися довгі коди
                src[i] = (byte)(int)(alphabet * Math.Pow(rnd.NextDouble(), 3));
            }

            var freq = new long[256];
            foreach (var b in src)
            {
                freq[b]++;
            }

            var lengths = codec.BuildLengths(freq);
            Assert.True(lengths.Max() <= HuffmanCodec.MaxCodeLength);
            Assert.True(lengths.Where(l => l > 0).Sum(l => Math.Pow(2, -l)) <= 1.0 + 1e-12);
            for (var s = 0; s < 256; s++)
            {
                Assert.Equal(freq[s] > 0, lengths[s] > 0);
            }

            var (data, bits) = codec.Encode(src, lengths);
            Assert.Equal((bits + 7) / 8, data.Length);
            Assert.Equal(src, codec.Decode(data, lengths, src.Length));
        }
    }

    [Fact]
    public void All_256_symbols_equiprobable_give_length_8()
    {
        var freq = Enumerable.Repeat(10L, 256).ToArray();
        var lengths = new HuffmanCodec().BuildLengths(freq);
        Assert.All(lengths, l => Assert.Equal(8, l));
    }

    [Fact]
    public void Equal_frequencies_are_deterministic()
    {
        var freq = new long[256];
        for (var s = 0; s < 40; s++)
        {
            freq[s * 3] = 5;
        }

        var codec = new HuffmanCodec();
        Assert.Equal(codec.BuildLengths(freq), codec.BuildLengths((long[])freq.Clone()));
    }

    [Fact]
    public void Fibonacci_frequencies_are_limited_to_15_bits_without_dropping_symbols()
    {
        var freq = new long[256];
        long a = 1, b = 1;
        for (var s = 0; s < 40; s++)
        {
            freq[s] = a;
            (a, b) = (b, a + b);
        }

        var lengths = new HuffmanCodec().BuildLengths(freq);
        Assert.Equal(40, lengths.Count(l => l > 0));
        Assert.Equal(HuffmanCodec.MaxCodeLength, lengths.Max());
    }

    [Fact]
    public void Package_merge_equals_huffman_cost_when_limit_is_not_binding()
    {
        var rnd = new Random(7);
        var codec = new HuffmanCodec();
        for (var round = 0; round < 100; round++)
        {
            var freq = new long[256];
            for (var s = 0; s < rnd.Next(2, 60); s++)
            {
                freq[rnd.Next(256)] = rnd.Next(1, 1000);
            }

            if (freq.Count(f => f > 0) < 2)
            {
                continue;
            }

            Assert.Equal(Cost(freq, codec.BuildLengths(freq)), Cost(freq, HuffmanCodec.BuildLengthsLimited(freq, 15)));
        }
    }

    [Fact]
    public void Package_merge_with_small_limit_is_valid_and_optimal_against_brute_force()
    {
        long[] f = [1, 1, 2, 3, 5, 8];
        var freq = new long[256];
        for (var i = 0; i < f.Length; i++)
        {
            freq[i] = f[i];
        }

        const int limit = 4;
        var lengths = HuffmanCodec.BuildLengthsLimited(freq, limit);
        Assert.InRange(lengths.Max(), 1, limit);
        Assert.True(lengths.Where(l => l > 0).Sum(l => Math.Pow(2, -l)) <= 1.0 + 1e-12);

        var best = long.MaxValue;
        var current = new int[f.Length];
        Search(0);
        Assert.Equal(best, Cost(freq, lengths));

        void Search(int i)
        {
            if (i == f.Length)
            {
                if (current.Sum(l => Math.Pow(2, -l)) <= 1.0 + 1e-12)
                {
                    best = Math.Min(best, Enumerable.Range(0, f.Length).Sum(k => f[k] * current[k]));
                }

                return;
            }

            for (var l = 1; l <= limit; l++)
            {
                current[i] = l;
                Search(i + 1);
            }
        }
    }

    [Fact]
    public void Limited_build_rejects_too_many_symbols()
    {
        var freq = Enumerable.Repeat(1L, 256).ToArray();
        Assert.Throws<ArgumentException>(() => HuffmanCodec.BuildLengthsLimited(freq, 7));
    }

    [Fact]
    public void Invalid_inputs_are_rejected()
    {
        var codec = new HuffmanCodec();
        Assert.Throws<ArgumentException>(() => codec.BuildLengths(new long[10]));
        var negative = new long[256];
        negative[3] = -1;
        Assert.Throws<ArgumentException>(() => codec.BuildLengths(negative));

        var lengths = new byte[256];
        lengths[0] = 1;
        lengths[1] = 1;
        Assert.Throws<InvalidDataException>(() => codec.Encode([2], lengths));
        Assert.Throws<ArgumentException>(() => HuffmanCodec.CanonicalCodes(new byte[3]));

        var tooLong = new byte[256];
        tooLong[0] = 16;
        Assert.Throws<InvalidDataException>(() => HuffmanCodec.CanonicalCodes(tooLong));

        var oversubscribed = new byte[256];
        oversubscribed[0] = 1;
        oversubscribed[1] = 1;
        oversubscribed[2] = 1;
        Assert.Throws<InvalidDataException>(() => HuffmanCodec.CanonicalCodes(oversubscribed));
    }

    [Fact]
    public void Decode_reports_truncated_and_foreign_streams()
    {
        var codec = new HuffmanCodec();
        var lengths = new byte[256];
        lengths[0] = 1;
        lengths[1] = 2;
        lengths[2] = 2;

        Assert.Throws<InvalidDataException>(() => codec.Decode([0b0100_0000], lengths, 20));

        var onlyZero = new byte[256];
        onlyZero[5] = 1;
        Assert.Throws<InvalidDataException>(() => codec.Decode([0xFF], onlyZero, 1));
    }

    private static long Cost(long[] freq, byte[] lengths) =>
        Enumerable.Range(0, 256).Sum(s => freq[s] * lengths[s]);
}
