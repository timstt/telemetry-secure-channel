using Telemetry.System;

namespace Telemetry.Tests.Acceptance;

/// <summary>
/// C1 №3. Спокійний сценарій: усі кадри відновлено точно.
/// Критерій: session.csv, сценарій «спокійний», 5 seed: множина відновлених кадрів == вхідна, побайтово.
/// </summary>
public class C1_Case03Tests
{
    [Fact]
    public void Case03_calm_scenario_recovers_every_frame_on_every_seed()
    {
        var runs = FullRun.Runs("спокійний").ToList();
        Assert.Equal(5, runs.Count);
        Assert.All(runs, r =>
        {
            Assert.Equal(RunStatus.Ok, r.Status);
            Assert.True(r.AllFramesRecovered, $"seed {r.Seed}: відновлено {r.RecoveredFrames} з {r.Frames}");
            Assert.Equal(3000, r.RecoveredFrames);
        });
    }
}
