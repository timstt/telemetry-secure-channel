using System.Text.Json;
using Telemetry.Core;
using Telemetry.Huffman;
using Telemetry.Packet;
using Telemetry.Tests.Infrastructure;

namespace Telemetry.Tests.Acceptance;

/// <summary>
/// C1 №12. Еталонні пакети приймаються; прогін 3 сценаріїв у ліміті часу.
/// Критерій: Усі ref_* Ok, усі bad_* — код з expected_failures.json; Runner 3 сценарії × 5 seed ≤ 120 с.
/// </summary>
public class C1_Case12Tests
{
    [Fact]
    public void Case12_reference_packets_accepted_and_bad_ones_give_expected_codes()
    {
        var receiver = new Receiver(FullRun.Password, new HuffmanCodec());
        foreach (var name in new[] { "ref_01_single_frame", "ref_02_empty", "ref_03_degenerate", "ref_04_session", "ref_05_no_compression", "ref_06_class_table" })
        {
            Assert.True(receiver.Accept(File.ReadAllBytes(TestData.Reference(name + ".bin"))).Ok, name);
        }

        using var doc = JsonDocument.Parse(File.ReadAllText(TestData.Reference("expected_failures.json")));
        foreach (var p in doc.RootElement.EnumerateObject())
        {
            var expected = Enum.Parse<PacketError>(p.Value.GetProperty("очікуваний_код").GetString()!);
            Assert.Equal(expected, new Receiver(FullRun.Password, new HuffmanCodec()).Accept(File.ReadAllBytes(TestData.Reference(p.Name))).Error);
        }
    }

    [Fact]
    public void Case12_full_run_fits_time_limit()
    {
        var report = FullRun.Report;
        Assert.Equal(15, report.Runs.Count);
        Assert.True(report.AllOk, FullRun.Log);
        Assert.False(report.TimeLimitExceeded);
        Assert.True(FullRun.WallSeconds <= FullRun.Config.FullRunLimitSeconds, $"{FullRun.WallSeconds:F1} с");
    }

    [Fact]
    public void Case12_log_and_results_contain_no_password()
    {
        Assert.DoesNotContain(FullRun.Password, FullRun.Log, StringComparison.Ordinal);
        foreach (var file in Directory.EnumerateFiles(FullRun.OutDir, "*", SearchOption.AllDirectories))
        {
            Assert.DoesNotContain(FullRun.Password, File.ReadAllText(file), StringComparison.Ordinal);
        }
    }
}
