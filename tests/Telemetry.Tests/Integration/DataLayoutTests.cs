using System.Security.Cryptography;
using System.Text.Json;
using Telemetry.Tests.Infrastructure;

namespace Telemetry.Tests.Integration;

/// <summary>Матеріали викладача на місці й не змінені.</summary>
public class DataLayoutTests
{
    /// <summary>SHA-256 виданого channel.py. Зміна файла заборонена (пастка 11).</summary>
    private const string ChannelPySha256 = "de402889efab937fc594448ce1c72523b7fbec9daf1ef87770468ee46234e734";

    [Fact]
    public void Channel_py_is_unchanged()
    {
        var hash = SHA256.HashData(File.ReadAllBytes(TestData.Path("channel", "channel.py")));
        Assert.Equal(ChannelPySha256, Convert.ToHexString(hash).ToLowerInvariant());
    }

    [Theory]
    [InlineData("ref_01_single_frame.bin", 107)]
    [InlineData("ref_02_empty.bin", 89)]
    [InlineData("ref_03_degenerate.bin", 93)]
    [InlineData("ref_04_session.bin", 771)]
    [InlineData("ref_05_no_compression.bin", 473)]
    [InlineData("ref_06_class_table.bin", 432)]
    [InlineData("bad_01_tampered_byte.bin", 771)]
    [InlineData("bad_02_tampered_tag.bin", 771)]
    [InlineData("bad_03_truncated.bin", 762)]
    [InlineData("bad_04_bad_magic.bin", 771)]
    [InlineData("bad_05_reserved.bin", 771)]
    public void Reference_packet_has_expected_size(string name, int size) =>
        Assert.Equal(size, new FileInfo(TestData.Reference(name)).Length);

    [Fact]
    public void Ref_json_length_matches_bin()
    {
        foreach (var json in Directory.GetFiles(TestData.Path("data", "reference"), "ref_*.json"))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(json));
            var expected = doc.RootElement.GetProperty("довжина_пакета").GetInt32();
            var bin = global::System.IO.Path.ChangeExtension(json, ".bin");
            Assert.Equal(expected, new FileInfo(bin).Length);
        }
    }

    [Fact]
    public void Expected_failures_cover_all_bad_packets()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(TestData.Reference("expected_failures.json")));
        var names = doc.RootElement.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).ToArray();
        var files = Directory.GetFiles(TestData.Path("data", "reference"), "bad_*.bin")
            .Select(global::System.IO.Path.GetFileName).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(files, names);
    }

    [Fact]
    public void Session_bin_is_12_bytes_per_csv_row()
    {
        var rows = File.ReadLines(TestData.Path("data", "session.csv")).Skip(1).Count(l => l.Length > 0);
        Assert.Equal(3000, rows);
        Assert.Equal(12L * rows, new FileInfo(TestData.Path("data", "session.bin")).Length);
    }

    [Fact]
    public void Train_has_known_class_imbalance()
    {
        // Пастка 17: 0:6243, 1:801, 2:956 — мажоритарний класифікатор дає macro-F1 ≈ 0.2925.
        var counts = File.ReadLines(TestData.Path("data", "train.csv")).Skip(1)
            .Where(l => l.Length > 0)
            .GroupBy(l => l[(l.LastIndexOf(',') + 1)..])
            .ToDictionary(g => g.Key, g => g.Count());
        Assert.Equal(6243, counts["0"]);
        Assert.Equal(801, counts["1"]);
        Assert.Equal(956, counts["2"]);
    }
}
