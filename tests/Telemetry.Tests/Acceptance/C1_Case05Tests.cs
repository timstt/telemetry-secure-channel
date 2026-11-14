using Telemetry.System;

namespace Telemetry.Tests.Acceptance;

/// <summary>
/// C1 №5. Аварійний сценарій: без аварійного завершення, Q обчислено.
/// Критерій: «аварійний», 5 seed: прогін завершується, лог без необроблених винятків, Q ∈ [0,1].
/// </summary>
public class C1_Case05Tests
{
    [Fact]
    public void Case05_emergency_scenario_completes_and_q_is_computed()
    {
        var runs = FullRun.Runs("аварійний").ToList();
        Assert.Equal(5, runs.Count);
        Assert.All(runs, r =>
        {
            Assert.Equal(RunStatus.Ok, r.Status);
            Assert.NotNull(r.Metric);
            Assert.InRange(r.Metric!.Q, 0, 1);
            Assert.True(r.PacketsOverBudget > 0); // бюджет справді вузький
        });
        Assert.DoesNotContain("Unhandled", FullRun.Log, StringComparison.Ordinal);
        Assert.DoesNotContain("ERROR", FullRun.Log, StringComparison.Ordinal);
    }
}
