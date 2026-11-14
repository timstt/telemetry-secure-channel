using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Telemetry.Core;
using Telemetry.System;

namespace Telemetry.Present;

internal sealed record RunProgress(
    string Stage,
    int Completed,
    int Total,
    string? LastScenario,
    int? LastSeed,
    string? LastStatus,
    double? LastQ,
    double? LastR,
    IReadOnlyDictionary<string, int> ByScenario);

/// <summary>
/// Прогін Runner для сторінки. Збій чи тайм-аут ніколи не прибирає дані з екрана: показується останній
/// успішний результат (збережений out/results.json або попередній живий прогін), а помилка йде окремим повідомленням.
/// </summary>
internal sealed class LiveRun(Func<string>? run = null, double timeoutSeconds = LiveRun.TimeoutSeconds)
{
    public const double TimeoutSeconds = 90;

    private readonly object gate = new();
    private string? resultsJson;
    private DateTime? updated;
    private string source = "немає даних";
    private bool running;
    private string? lastError;
    private RunProgress? progress;
    private double lastSeconds;
    private int runs;
    private long runId;
    private Task? worker;

    public event Action<string>? Completed;

    public Task? Worker => worker;

    public bool HasData
    {
        get
        {
            lock (gate)
            {
                return resultsJson is not null;
            }
        }
    }

    public string? ResultJson()
    {
        lock (gate)
        {
            return resultsJson;
        }
    }

    /// <summary>Підхоплює останній збережений прогін Runner, якщо він є й читається.</summary>
    public void LoadSnapshot()
    {
        var path = Path.Combine(Repo.Out, SessionRunner.ResultsJson);
        try
        {
            var text = File.ReadAllText(path);
            _ = JsonNode.Parse(text)!["Scenarios"] ?? throw new InvalidDataException("немає Scenarios");
            lock (gate)
            {
                resultsJson = text;
                updated = File.GetLastWriteTime(path);
                source = "збережений прогін Runner";
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or global::System.Text.Json.JsonException or NullReferenceException)
        {
            lastError = null;
        }
    }

    /// <summary>Запускає прогін у фоні; false, якщо попередній ще йде.</summary>
    public bool TryStart()
    {
        lock (gate)
        {
            if (running || (worker is { IsCompleted: false }))
            {
                return false;
            }

            running = true;
            lastError = null;
            progress = Preparing();
            var id = ++runId;
            var task = Task.Run(() => Execute(id));
            worker = task;
            _ = Task.Delay(TimeSpan.FromSeconds(timeoutSeconds)).ContinueWith(_ => Timeout(task, id), TaskScheduler.Default);
            return true;
        }
    }

    /// <summary>Синхронний прогін (перший запуск, коли немає збережених результатів).</summary>
    public bool RunNow()
    {
        long id;
        lock (gate)
        {
            running = true;
            lastError = null;
            progress = Preparing();
            id = ++runId;
        }

        Execute(id);
        return HasData;
    }

    public JsonObject State()
    {
        lock (gate)
        {
            return new JsonObject
            {
                ["running"] = running,
                ["hasData"] = resultsJson is not null,
                ["source"] = source,
                ["updated"] = updated?.ToString("HH:mm:ss", global::System.Globalization.CultureInfo.InvariantCulture),
                ["runs"] = runs,
                ["lastSeconds"] = lastSeconds,
                ["error"] = lastError,
                ["progress"] = progress is null ? null : JsonSerializer.SerializeToNode(progress, DemoData.Json),
                ["results"] = resultsJson is null ? null : JsonNode.Parse(resultsJson),
            };
        }
    }

    private void Timeout(Task task, long id)
    {
        lock (gate)
        {
            if (running && id == runId && ReferenceEquals(task, worker))
            {
                running = false;
                progress = progress is null ? null : progress with { Stage = "timeout" };
                lastError = $"Прогін не вклався в {timeoutSeconds:F0} с. На екрані лишилися попередні результати.";
            }
        }
    }

    private void Execute(long id)
    {
        var watch = Stopwatch.StartNew();
        try
        {
            var json = (run ?? (() => RunOnce(Path.Combine(Repo.Out, "present-live"), id)))();
            lock (gate)
            {
                if (!running || id != runId)
                {
                    return;
                }

                resultsJson = json;
                updated = DateTime.Now;
                source = "живий прогін";
                runs++;
                lastSeconds = watch.Elapsed.TotalSeconds;
                running = false;
                progress = progress is null ? null : progress with { Stage = "done" };
            }

            Completed?.Invoke(json);
        }
#pragma warning disable CA1031 // під час показу будь-який збій прогону не має ні зупиняти сторінку, ні прибирати дані
        catch (Exception ex)
#pragma warning restore CA1031
        {
            lock (gate)
            {
                if (!running || id != runId)
                {
                    return;
                }

                lastError = "Прогін не вдався: " + ex.Message.TrimEnd('.') + "." + (resultsJson is null ? "" : " На екрані лишилися попередні результати.");
                lastSeconds = watch.Elapsed.TotalSeconds;
                running = false;
                progress = progress is null ? null : progress with { Stage = "failed" };
            }
        }
    }

    private static RunProgress Preparing() => new("preparing", 0, 0, null, null, null, null, null, new Dictionary<string, int>());

    private void SetProgress(long id, string[] scenarios, int seeds)
    {
        lock (gate)
        {
            if (!running || id != runId)
            {
                return;
            }

            progress = new RunProgress("running", 0, scenarios.Length * seeds, null, null, null, null, null,
                scenarios.ToDictionary(scenario => scenario, _ => 0));
        }
    }

    private void UpdateProgress(long id, RunResult result, int completed, int total)
    {
        lock (gate)
        {
            if (!running || id != runId || progress is null)
            {
                return;
            }

            var counts = new Dictionary<string, int>(progress.ByScenario);
            counts[result.Scenario] = counts.GetValueOrDefault(result.Scenario) + 1;
            progress = new RunProgress(completed == total ? "saving" : "running", completed, total,
                result.Scenario, result.Seed, result.Status.ToString(), result.Metric?.Q, result.Metric?.R, counts);
        }
    }

    private string RunOnce(string liveDir, long id)
    {
        var configJson = File.ReadAllText(Repo.Config);
        var config = TeamConfig.Parse(configJson);
        Directory.CreateDirectory(liveDir);
        using var log = new RunLog(Path.Combine(liveDir, "runner.log"), [config.SessionPassword]);
        var bridge = ChannelBridge.FindBridgeScript();
        var runner = new SessionRunner(
            (scenario, seed) => new ChannelBridge(ChannelBridge.ScenarioJson(configJson, scenario), seed, bridge),
            () => new TelemetrySystem(config, new Scheduler()),
            log);
        var scenarios = config.Channel.Scenarios.Keys.ToArray();
        var seeds = RunSeeds.Default(config.Channel.Seed);
        SetProgress(id, scenarios, seeds.Count);
        var report = runner.Run(
            () => SessionCsv.Load(Repo.Session, log),
            new RunSettings(config, scenarios, seeds, liveDir, config.FullRunLimitSeconds),
            (result, completed, total) => UpdateProgress(id, result, completed, total));
        if (!report.AllOk)
        {
            throw new InvalidOperationException("частина прогонів не завершилась, деталі в out/present-live/runner.log");
        }

        return File.ReadAllText(Path.Combine(liveDir, SessionRunner.ResultsJson));
    }
}
