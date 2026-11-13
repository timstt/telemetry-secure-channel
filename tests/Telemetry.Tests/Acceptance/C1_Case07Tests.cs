using Telemetry.Huffman;
using Telemetry.Tests.Huffman;

namespace Telemetry.Tests.Acceptance;

/// <summary>
/// C1 №7. Вироджений алфавіт кодується коректно.
/// Критерій: Один символ → length=1, код «0»; ref_03 читається; encode→decode тотожність.
/// </summary>
public class C1_Case07Tests
{
    [Fact]
    public void Case07()
    {
        var codec = new HuffmanCodec();
        var reference = ReferenceFile.Load("ref_03_degenerate");
        var entry = Assert.Single(reference.Table);
        Assert.Equal(1, entry.Length);
        Assert.Equal("0", entry.Code);

        var freq = new long[256];
        foreach (var b in reference.FrameBytes)
        {
            freq[b]++;
        }

        var lengths = codec.BuildLengths(freq);
        Assert.Equal(reference.Lengths(), lengths);
        Assert.Equal(0u, HuffmanCodec.CanonicalCodes(lengths)[entry.Symbol]);

        var (data, _) = codec.Encode(reference.FrameBytes, lengths);
        Assert.Equal(reference.Bitstream, data);
        Assert.Equal(reference.FrameBytes, codec.Decode(data, lengths, reference.FrameBytes.Length));
    }
}
