using Telemetry.Huffman;
using Telemetry.Tests.Huffman;

namespace Telemetry.Tests.Acceptance;

/// <summary>
/// C1 №6. Дерево Гаффмана відновлюється з канонічних довжин.
/// Критерій: Довжини ref_01..06 → канонічні коди за §3.3 == «код» у ref_*.json; декодування == кадри json.
/// </summary>
public class C1_Case06Tests
{
    [Theory]
    [InlineData("ref_01_single_frame")]
    [InlineData("ref_02_empty")]
    [InlineData("ref_03_degenerate")]
    [InlineData("ref_04_session")]
    [InlineData("ref_06_class_table")]
    public void Case06(string name)
    {
        var reference = ReferenceFile.Load(name);
        var lengths = reference.Lengths();
        var codes = HuffmanCodec.CanonicalCodes(lengths);
        foreach (var (symbol, length, code) in reference.Table)
        {
            Assert.Equal(code, Convert.ToString(codes[symbol], 2).PadLeft(length, '0'));
        }

        Assert.Equal(
            reference.FrameBytes,
            new HuffmanCodec().Decode(reference.Bitstream, lengths, 12 * reference.FrameCount));
    }

    [Fact]
    public void Case06_no_compression_reference_is_raw_frames()
    {
        var reference = ReferenceFile.Load("ref_05_no_compression");
        Assert.Empty(reference.Table);
        Assert.Equal(reference.FrameBytes, reference.Bitstream);
    }
}
