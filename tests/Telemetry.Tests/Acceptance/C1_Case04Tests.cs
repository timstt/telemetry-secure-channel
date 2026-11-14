using Telemetry.System;

namespace Telemetry.Tests.Acceptance;

/// <summary>
/// C1 №4. Робочий сценарій: частка втрачених критичних ≤ порогу викладача.
/// Критерій: «робочий», 5 seed: 1 − R ≤ 0.05 на КОЖНОМУ seed (запас малий, пастка 21).
/// </summary>
public class C1_Case04Tests
{
    [Fact]
    public void Case04_working_scenario_critical_loss_within_threshold_on_each_seed()
    {
        var threshold = FullRun.Config.CriticalLossThresholds["робочий"];
        var runs = FullRun.Runs("робочий").ToList();
        Assert.Equal(5, runs.Count);
        Assert.All(runs, r =>
        {
            Assert.Equal(RunStatus.Ok, r.Status);
            Assert.True(1 - r.Metric!.R <= threshold + 1e-12, $"seed {r.Seed}: 1 − R = {1 - r.Metric.R:F4}");
            Assert.True(r.CriticalLossWithinThreshold);
        });
    }
}
