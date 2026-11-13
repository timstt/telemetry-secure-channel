using Telemetry.Core;
using Telemetry.Tests.Infrastructure;

namespace Telemetry.Tests.Integration;

public class ConfigTests
{
    private static TeamConfig Load() => TeamConfig.Load(TestData.Path("data", "team_config.json"));

    [Fact]
    public void Loads_team_config_with_expected_values()
    {
        var cfg = Load();

        Assert.Equal("T01", cfg.Team);
        Assert.Equal(1, cfg.FormatVersion);
        Assert.Equal(512, cfg.Packing.MaxPacketBytes);
        Assert.Equal(437, cfg.Packing.MaxPayloadBytes);
        Assert.Equal(64, cfg.Packing.CandidateWindowFrames);
        Assert.Equal(0, cfg.Packing.ClassTableInPacking);
        Assert.Equal(425243, cfg.Channel.Seed);
        Assert.Equal(120, cfg.FullRunLimitSeconds);
        Assert.Equal(0.58, cfg.Perceptron.F1Min, 10);
    }

    [Fact]
    public void Metric_coefficients_match_trap_14()
    {
        var m = Load().Metric;
        Assert.Equal(0.5, m.Alpha, 10);
        Assert.Equal(0.35, m.Beta, 10);
        Assert.Equal(0.15, m.Gamma, 10);
        Assert.Equal(20.0, m.TRefSeconds, 10);
    }

    [Fact]
    public void Scenarios_are_typed_including_burst_and_budget()
    {
        var sc = Load().Channel.Scenarios;
        Assert.Equal(3, sc.Count);

        var calm = sc["спокійний"];
        Assert.Equal(64000, calm.BandwidthBps);
        Assert.Equal("стала", calm.Delay.Kind);
        Assert.Equal(20, calm.Delay.Value);
        Assert.Equal(1.0, calm.MeanBurstLength);

        var emergency = sc["аварійний"];
        Assert.Equal(0.15, emergency.LossProbability, 10);
        Assert.Equal(3.0, emergency.MeanBurstLength);
        Assert.Equal(16.9, emergency.TransmissionBudgetSeconds);
        Assert.Equal(50, emergency.Delay.From);
        Assert.Equal(500, emergency.Delay.To);
    }

    [Theory]
    [InlineData(0, false, 1)]
    [InlineData(2, false, 3)]
    [InlineData(1, false, 5)]
    [InlineData(0, true, 6)]
    [InlineData(1, true, 10)]
    public void Frame_value_is_priority_plus_critical_bonus(int cls, bool critical, int expected) =>
        Assert.Equal(expected, Load().Priorities.Value(cls, critical));

    [Fact]
    public void Unknown_class_is_rejected() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Load().Priorities.ForClass(3));

    [Theory]
    [InlineData(0, 3384)]   // NO_COMPRESSION: 8·(437 − 14)
    [InlineData(7, 3272)]   // ref_01: 8·(437 − 14 − 14)
    [InlineData(129, 1320)] // ≈ вікно 64 кадри (FORMAT_SPEC §3.5: W ≈ 1300)
    public void Knapsack_capacity_formula(int tableNsym, int expectedBits) =>
        Assert.Equal(expectedBits, Load().Packing.CapacityBits(tableNsym));

    [Fact]
    public void Critical_loss_thresholds_are_read() =>
        Assert.Equal(0.05, Load().CriticalLossThresholds["робочий"], 10);

    [Fact]
    public void Invalid_json_gives_InvalidDataException() =>
        Assert.Throws<InvalidDataException>(() => TeamConfig.Parse("{ не json"));

    [Fact]
    public void Null_document_gives_InvalidDataException() =>
        Assert.Throws<InvalidDataException>(() => TeamConfig.Parse("null"));

    [Fact]
    public void Inconsistent_payload_limit_is_rejected()
    {
        var json = File.ReadAllText(TestData.Path("data", "team_config.json"))
            .Replace("\"max_payload_bytes\": 437", "\"max_payload_bytes\": 500", StringComparison.Ordinal);
        var ex = Assert.Throws<InvalidDataException>(() => TeamConfig.Parse(json));
        Assert.Contains("max_payload_bytes", ex.Message, StringComparison.Ordinal);
    }
}
