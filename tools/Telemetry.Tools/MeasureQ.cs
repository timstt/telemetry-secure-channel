using System.Globalization;
using System.Text;
using Telemetry.Core;
using Telemetry.Optimize;
using Telemetry.System;

namespace Telemetry.Tools;

/// <summary>
/// Заміри Q через справжній channel.py (тімлід):
/// <list type="bullet">
/// <item><c>measure-q [--seeds a,b,…] [--scenarios x,y,…]</c> — таблиця 4: S, R, Q за сценаріями (середнє за seed);</item>
/// <item><c>measure-q --ablation</c> — таблиця 2: повна система і система з вимкненим по черзі кожним блоком;</item>
/// <item><c>optimize [--pop N] [--gens G]</c> — таблиця 5: ГА проти випадкового пошуку з однаковим бюджетом обчислень,
/// журнали в results/ga/.</item>
/// </list>
/// </summary>
internal static class MeasureQ
{
    private static readonly (string Name, Func<TeamConfig, ISessionSystem> Make)[] Variants =
    [
        ("повна система", c => new TelemetrySystem(c, new Scheduler())),
        ("без класифікатора (усі клас 0)", c => new TelemetrySystem(c, new Scheduler(), new SystemOptions(UseClassifier: false))),
        ("без стиснення (NO_COMPRESSION)", c => new TelemetrySystem(c, new Scheduler(), new SystemOptions(new SenderOptions(Compression: CompressionMode.None)))),
        ("без ДП (жадібний рюкзак)", c => new TelemetrySystem(c, new Scheduler(), new SystemOptions(UseDpPacker: false))),
        ("без рюкзака (заповнення по черзі)", c => new TelemetrySystem(c, new Scheduler(), new SystemOptions(new SenderOptions(UseKnapsack: false)))),
        ("без планувальника (FIFO)", c => new TelemetrySystem(c, new FifoScheduler())),
        ("без дублювання критичних", c => new TelemetrySystem(c, new Scheduler(), new SystemOptions(new SenderOptions(DuplicateCritical: false)))),
    ];

    public static int Run(string[] args)
    {
        var root = RepoPaths.Root();
        var configJson = File.ReadAllText(Path.Combine(root, "data", "team_config.json"));
        var config = TeamConfig.Parse(configJson);
        var seeds = Option(args, "--seeds") is { } s ? RunSeeds.Parse(s) : RunSeeds.Default(config.Channel.Seed);
        var scenarios = Option(args, "--scenarios")?.Split(',') ?? [.. config.Channel.Scenarios.Keys];
        var frames = SessionCsv.Load(Path.Combine(root, "data", "session.csv"), new RunLog(TextWriter.Null, []));

        var report = new StringBuilder();
        if (args.Contains("--ablation"))
        {
            report.AppendLine("Таблиця 2. Вплив кожного блоку: середнє Q за seed " + string.Join(", ", seeds));
            report.AppendLine();
            report.AppendLine("| Варіант | " + string.Join(" | ", scenarios.Select(x => "Q " + x)) + " | Q середнє |");
            report.AppendLine("|---|" + string.Concat(scenarios.Select(_ => "---:|")) + "---:|");
            foreach (var (name, make) in Variants)
            {
                var r = Measure(configJson, config, frames, scenarios, seeds, () => make(config));
                report.AppendLine(CultureInfo.InvariantCulture, $"| {name} | {string.Join(" | ", r.Scenarios.Select(x => x.MeanQ.ToString("F4", CultureInfo.InvariantCulture)))} | {r.Scenarios.Average(x => x.MeanQ):F4} |");
            }
        }
        else
        {
            var r = Measure(configJson, config, frames, scenarios, seeds, () => new TelemetrySystem(config, new Scheduler()));
            report.AppendLine("Таблиця 4. Поведінка за втрат: середнє за seed " + string.Join(", ", seeds));
            report.AppendLine();
            report.AppendLine("| Сценарій | S | R | R мін. | Q | доставлено пакетів | невміщено в бюджет | час обчислень, с |");
            report.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|");
            foreach (var sc in scenarios)
            {
                var runs = r.Runs.Where(x => x.Scenario == sc && x.Metric is not null).ToList();
                report.AppendLine(CultureInfo.InvariantCulture,
                    $"| {sc} | {runs.Average(x => x.Metric!.S):F4} | {runs.Average(x => x.Metric!.R):F4} | {runs.Min(x => x.Metric!.R):F4} | {runs.Average(x => x.Metric!.Q):F4} | {runs.Average(x => x.PacketsDelivered):F1} | {runs.Average(x => x.PacketsOverBudget):F1} | {runs.Average(x => x.ComputeSeconds):F3} |");
            }

            report.AppendLine();
            report.AppendLine(CultureInfo.InvariantCulture, $"Повний прогін: {r.ElapsedSeconds:F2} с (ліміт {config.FullRunLimitSeconds} с).");
        }

        return Write(root, args.Contains("--ablation") ? "ablation.md" : "measure-q.md", report.ToString());
    }

    public static int Optimize(string[] args)
    {
        var root = RepoPaths.Root();
        var configJson = File.ReadAllText(Path.Combine(root, "data", "team_config.json"));
        var config = TeamConfig.Parse(configJson);
        var frames = SessionCsv.Load(Path.Combine(root, "data", "session.csv"), new RunLog(TextWriter.Null, []));
        var settings = new GaSettings(
            Population: int.Parse(Option(args, "--pop") ?? "10", CultureInfo.InvariantCulture),
            Generations: int.Parse(Option(args, "--gens") ?? "10", CultureInfo.InvariantCulture));

        // Fitness — середнє Q за 3 сценаріями на двох seed, ВІДМІННИХ від відкритого (щоб не підганяти під нього).
        IReadOnlyList<int> seeds = [config.Channel.Seed + 100, config.Channel.Seed + 101];
        string[] scenarios = [.. config.Channel.Scenarios.Keys];
        double Fitness(Params p)
        {
            var r = Measure(configJson, config, frames, scenarios, seeds,
                () => new TelemetrySystem(config, new Scheduler(), new SystemOptions(new SenderOptions(Params: p))));
            var q = r.Scenarios.Average(x => x.MeanQ);
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  {p.HuffmanWindowFrames,3} {p.NoCompressionThreshold:F3} {p.PriorityWeight:F3} -> {q:F5}"));
            return q;
        }

        Console.WriteLine($"ГА: популяція {settings.Population}, поколінь {settings.Generations}, бюджет {settings.Evaluations} обчислень");
        var ga = new GeneticAlgorithm(settings);
        var (gaBest, gaLog) = ga.Run(Fitness, seed: 1);
        Console.WriteLine("Випадковий пошук з тим самим бюджетом");
        var (rsBest, rsLog) = new RandomSearch(settings).Run(Fitness, seed: 1);
        var baseline = Fitness(SenderOptions.DefaultParams);

        var dir = Path.Combine(root, "results", "ga");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "ga_log.csv"), GeneticAlgorithm.ToCsv(gaLog));
        File.WriteAllText(Path.Combine(dir, "random_search_log.csv"), GeneticAlgorithm.ToCsv(rsLog));

        var sb = new StringBuilder();
        sb.AppendLine("# Таблиця 5. Збіжність ГА проти випадкового пошуку");
        sb.AppendLine();
        sb.AppendLine(CultureInfo.InvariantCulture, $"Fitness — середнє Q за 3 сценаріями на seed {string.Join(", ", seeds)}; бюджет {settings.Evaluations} обчислень (ГА реально обчислив {ga.EvaluationsUsed} унікальних особин завдяки кешу).");
        sb.AppendLine();
        sb.AppendLine("| Покоління | ГА best | ГА mean | Випадковий пошук best | ВП mean |");
        sb.AppendLine("|---:|---:|---:|---:|---:|");
        for (var g = 0; g < gaLog.Generations.Count; g++)
        {
            var a = gaLog.Generations[g];
            var b = rsLog.Generations[g];
            sb.AppendLine(CultureInfo.InvariantCulture, $"| {g} | {a.Best:F5} | {a.Mean:F5} | {b.Best:F5} | {b.Mean:F5} |");
        }

        sb.AppendLine();
        sb.AppendLine(CultureInfo.InvariantCulture, $"Найкраще ГА: вікно {gaBest.HuffmanWindowFrames}, поріг {gaBest.NoCompressionThreshold:F3}, вага {gaBest.PriorityWeight:F3} → Q = {gaLog.Generations[^1].Best:F5}.");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Найкраще ВП: вікно {rsBest.HuffmanWindowFrames}, поріг {rsBest.NoCompressionThreshold:F3}, вага {rsBest.PriorityWeight:F3} → Q = {rsLog.Generations[^1].Best:F5}.");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Параметри за замовчуванням ({SenderOptions.DefaultParams.HuffmanWindowFrames}, {SenderOptions.DefaultParams.NoCompressionThreshold}, {SenderOptions.DefaultParams.PriorityWeight}) → Q = {baseline:F5}.");
        File.WriteAllText(Path.Combine(dir, "summary.md"), sb.ToString(), new UTF8Encoding(false));
        Console.Write(sb.ToString());
        return 0;
    }

    private static RunReport Measure(
        string configJson, TeamConfig config, IReadOnlyList<Frame> frames, IReadOnlyList<string> scenarios,
        IReadOnlyList<int> seeds, Func<ISessionSystem> system)
    {
        var bridge = ChannelBridge.FindBridgeScript();
        var outDir = Path.Combine(Path.GetTempPath(), "telemetry-measure-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var log = new RunLog(TextWriter.Null, [config.SessionPassword]);
            var runner = new SessionRunner(
                (scenario, seed) => new ChannelBridge(ChannelBridge.ScenarioJson(configJson, scenario), seed, bridge),
                system,
                log);
            return runner.Run(() => frames, new RunSettings(config, scenarios, seeds, outDir, double.MaxValue));
        }
        finally
        {
            try
            {
                Directory.Delete(outDir, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    private static int Write(string root, string file, string text)
    {
        Console.Write(text);
        var dir = Path.Combine(root, "out");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, file), text, new UTF8Encoding(false));
        return 0;
    }

    private static string? Option(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
