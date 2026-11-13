using Telemetry.Core;
using Telemetry.System;
using Telemetry.Tests.Infrastructure;

namespace Telemetry.Tests.SystemZone;

/// <summary>Пастка 14: метрика Q, одиниці і правила нуля.</summary>
public class MetricTests
{
    private const double A = 0.5, B = 0.35, G = 0.15, TRef = 20;

    [Fact]
    public void Trap14_metric_units_and_components()
    {
        var q = Metric.Compute(36000 * 8, 36000 * 8, 1.0, 0.0, A, B, G, TRef, receiverCrashed: false);
        Assert.Equal(0.5, q.S, 10);
        Assert.Equal(1.0, q.R, 10);
        Assert.Equal(1.0, q.T, 10);
        Assert.Equal((0.5 * 0.5) + 0.35 + 0.15, q.Q, 10);
    }

    [Fact]
    public void T_halves_when_run_time_equals_t_ref() =>
        Assert.Equal(0.5, Metric.Compute(1, 1, 1, 20, A, B, G, TRef, false).T, 10);

    [Fact]
    public void Zero_R_gives_zero_Q_but_keeps_components()
    {
        var q = Metric.Compute(100, 50, 0.0, 1, A, B, G, TRef, false);
        Assert.Equal(0, q.Q);
        Assert.Equal(100.0 / 150, q.S, 10);
    }

    [Fact]
    public void Receiver_crash_gives_zero_Q() =>
        Assert.Equal(0, Metric.Compute(1, 1, 1.0, 1, A, B, G, TRef, receiverCrashed: true).Q);

    [Fact]
    public void Nothing_sent_gives_S_equal_1() =>
        Assert.Equal(1.0, Metric.Compute(0, 0, 1.0, 0, A, B, G, TRef, false).S);

    [Fact]
    public void Q_is_within_unit_interval_for_valid_inputs()
    {
        var rng = new Random(1);
        for (var i = 0; i < 1000; i++)
        {
            var q = Metric.Compute(rng.NextInt64(0, 1_000_000), rng.NextInt64(0, 1_000_000), rng.NextDouble(), rng.NextDouble() * 200, A, B, G, TRef, false);
            Assert.InRange(q.Q, 0.0, 1.0);
        }
    }

    [Theory]
    [InlineData(-1, 0, 0.5, 0, 20)]
    [InlineData(0, -1, 0.5, 0, 20)]
    [InlineData(0, 0, 1.5, 0, 20)]
    [InlineData(0, 0, -0.1, 0, 20)]
    [InlineData(0, 0, double.NaN, 0, 20)]
    [InlineData(0, 0, 0.5, -1, 20)]
    [InlineData(0, 0, 0.5, 0, 0)]
    public void Invalid_arguments_are_rejected(long bRef, long bSent, double r, double t, double tRef) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Metric.Compute(bRef, bSent, r, t, A, B, G, tRef, false));

    [Fact]
    public void Overload_uses_team_config_coefficients()
    {
        var cfg = TeamConfig.Load(TestData.Path("data", "team_config.json")).Metric;
        var q = Metric.Compute(cfg, 100, 100, 1.0, 0, receiverCrashed: false);
        Assert.Equal((0.5 * 0.5) + 0.35 + 0.15, q.Q, 10);
        Assert.Throws<ArgumentNullException>(() => Metric.Compute(null!, 1, 1, 1, 0, false));
    }
}
