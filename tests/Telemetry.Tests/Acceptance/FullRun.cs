using System.Diagnostics;
using Telemetry.Core;
using Telemetry.System;
using Telemetry.Tests.Infrastructure;

namespace Telemetry.Tests.Acceptance;

/// <summary>
/// Один справжній повний прогін (session.csv, 3 сценарії × 5 seed, незмінений channel.py через bridge.py,
/// повна система команди) на всі приймальні кейси №3–5 і №12: рахується раз на запуск тестів.
/// </summary>
internal static class FullRun
{
    public const string Password = "algo2026";

    private static readonly Lazy<(RunReport Report, string Log, double WallSeconds, string OutDir)> Lazy = new(Execute);

    public static TeamConfig Config { get; } = TeamConfig.Load(TestData.Path("data", "team_config.json"));

    public static RunReport Report => Lazy.Value.Report;

    public static string Log => Lazy.Value.Log;

    public static double WallSeconds => Lazy.Value.WallSeconds;

    public static string OutDir => Lazy.Value.OutDir;

    public static IEnumerable<RunResult> Runs(string scenario) => Report.Runs.Where(r => r.Scenario == scenario);

    private static (RunReport, string, double, string) Execute()
    {
        var json = File.ReadAllText(TestData.Path("data", "team_config.json"));
        var bridge = TestData.Path("channel", "bridge.py");
        var outDir = Path.Combine(Path.GetTempPath(), "telemetry-acceptance-" + Guid.NewGuid().ToString("N"));
        using var text = new StringWriter();
        var watch = Stopwatch.StartNew();
        RunReport report;
        using (var log = new RunLog(text, [Config.SessionPassword]))
        {
            var runner = new SessionRunner(
                (scenario, seed) => new ChannelBridge(ChannelBridge.ScenarioJson(json, scenario), seed, bridge),
                () => new TelemetrySystem(Config, new Scheduler()),
                log);
            report = runner.Run(
                () => SessionCsv.Load(TestData.Path("data", "session.csv"), log),
                new RunSettings(Config, [.. Config.Channel.Scenarios.Keys], RunSeeds.Default(Config.Channel.Seed), outDir, Config.FullRunLimitSeconds));
        }

        return (report, text.ToString(), watch.Elapsed.TotalSeconds, outDir);
    }
}
