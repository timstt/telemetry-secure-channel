// Runner: повний прогін 3 сценарії × 5 seed (C1 №3–5, №12; C2 п.3; C5 п.3–4). Власник: тімлід.
//
//   dotnet run --project src/Telemetry.Runner [-- --config data/team_config.json] [--session data/session.csv]
//       [--out out] [--seeds 425243,425244,…] [--scenarios спокійний,робочий,аварійний] [--limit 120]
//
// Результати: out/results.json, out/results.csv, out/recovered/<сценарій>_<seed>.csv (атомарний запис),
// журнал: out/runner.log (усі винятки, без пароля).
// Коди виходу: 0 — усі прогони завершено в ліміті часу; 1 — є незавершені прогони; 2 — помилка аргументів
// або конфігурації; 3 — перевищено ліміт часу повного прогону.
using System.Globalization;
using System.Text;
using Telemetry.Core;
using Telemetry.System;

Console.OutputEncoding = new UTF8Encoding(false);

Dictionary<string, string> options;
try
{
    options = ParseArgs(args);
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine("Telemetry.Runner: " + ex.Message);
    Console.Error.WriteLine("Використання: dotnet run --project src/Telemetry.Runner -- [--config шлях] [--session шлях] [--out тека] [--seeds a,b,…] [--scenarios x,y,…] [--limit с]");
    return 2;
}

var configPath = options.TryGetValue("config", out var c) ? c : FindUp(Path.Combine("data", "team_config.json"));
var sessionPath = options.TryGetValue("session", out var s) ? s : FindUp(Path.Combine("data", "session.csv"));
var outDir = options.TryGetValue("out", out var o) ? o : Path.Combine(RepoRootOr(Directory.GetCurrentDirectory()), "out");

TeamConfig config;
string configJson;
IReadOnlyList<int> seeds;
IReadOnlyList<string> scenarios;
double limit;
try
{
    configJson = File.ReadAllText(configPath);
    config = TeamConfig.Parse(configJson);
    seeds = options.TryGetValue("seeds", out var seedText) ? RunSeeds.Parse(seedText) : RunSeeds.Default(config.Channel.Seed);
    scenarios = options.TryGetValue("scenarios", out var scText)
        ? scText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        : [.. config.Channel.Scenarios.Keys];
    limit = options.TryGetValue("limit", out var limitText)
        ? double.Parse(limitText, NumberStyles.Float, CultureInfo.InvariantCulture)
        : config.FullRunLimitSeconds;
}
catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or FormatException)
{
    Console.Error.WriteLine("Telemetry.Runner: " + ex.Message);
    return 2;
}

using var log = new RunLog(Path.Combine(outDir, "runner.log"), [config.SessionPassword]);
AppDomain.CurrentDomain.UnhandledException += (_, e) => log.Unhandled(e.ExceptionObject);

try
{
    log.Info($"конфігурація: {Path.GetFullPath(configPath)}; сеанс: {Path.GetFullPath(sessionPath)}; результати: {Path.GetFullPath(outDir)}");
    var bridge = ChannelBridge.FindBridgeScript();
    var runner = new SessionRunner(
        (scenario, seed) => new ChannelBridge(ChannelBridge.ScenarioJson(configJson, scenario), seed, bridge),
        () => new TelemetrySystem(config.SessionPassword, new Scheduler()),
        log);
    var report = runner.Run(
        () => SessionCsv.Load(sessionPath, log),
        new RunSettings(config, scenarios, seeds, outDir, limit));

    Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{"сценарій",-10} {"завершено",9} {"Q сер.",8} {"R сер.",8} {"R мін.",8}"));
    foreach (var sum in report.Scenarios)
    {
        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{sum.Scenario,-10} {sum.Completed,5}/{sum.Runs,-3} {sum.MeanQ,8:F4} {sum.MeanR,8:F4} {sum.MinR,8:F4}"));
    }

    Console.WriteLine(string.Create(
        CultureInfo.InvariantCulture,
        $"час: {report.ElapsedSeconds:F2} с із {report.TimeLimitSeconds} с; журнал: {Path.Combine(outDir, "runner.log")}; помилок у журналі: {log.ErrorCount}"));

    return report.TimeLimitExceeded ? 3 : report.AllOk ? 0 : 1;
}
catch (Exception ex)
{
    log.Error("Runner", ex);
    Console.Error.WriteLine("Telemetry.Runner: прогін перервано, деталі в журналі: " + ex.GetType().Name);
    return 1;
}

static Dictionary<string, string> ParseArgs(string[] args)
{
    string[] known = ["config", "session", "out", "seeds", "scenarios", "limit"];
    var result = new Dictionary<string, string>(StringComparer.Ordinal);
    for (var i = 0; i < args.Length; i++)
    {
        var key = args[i].StartsWith("--", StringComparison.Ordinal) ? args[i][2..] : "";
        if (!known.Contains(key))
        {
            throw new ArgumentException($"невідомий аргумент «{args[i]}»");
        }

        if (i + 1 >= args.Length)
        {
            throw new ArgumentException($"після «{args[i]}» потрібне значення");
        }

        result[key] = args[++i];
    }

    return result;
}

// Шукає файл відносно поточної теки, потім теки програми — вгору до кореня диска.
static string FindUp(string relative)
{
    foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
    {
        for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, relative);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }
    }

    return relative;
}

// Корінь репозиторію (тека з Telemetry.sln) або запасна тека.
static string RepoRootOr(string fallback)
{
    var sln = FindUp("Telemetry.sln");
    return File.Exists(sln) ? Path.GetDirectoryName(Path.GetFullPath(sln))! : fallback;
}
