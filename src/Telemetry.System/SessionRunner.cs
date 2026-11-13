using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Telemetry.Core;

namespace Telemetry.System;

/// <summary>Стан одного прогону «сценарій × seed».</summary>
public enum RunStatus
{
    /// <summary>Прогін завершено, Q обчислено.</summary>
    Ok,

    /// <summary>Приймач аварійно завершився: Q = 0 (Trap 14), виняток у журналі.</summary>
    ReceiverCrashed,

    /// <summary>Прогін не завершено (виняток передавача, каналу або даних), Q не обчислено.</summary>
    Failed,

    /// <summary>Прогін не запускався: вичерпано ліміт часу повного прогону.</summary>
    Skipped,
}

/// <summary>Результат одного прогону «сценарій × seed».</summary>
public sealed record RunResult(
    string Scenario,
    int Seed,
    RunStatus Status,
    int Frames,
    int CriticalFrames,
    int RecoveredFrames,
    int RecoveredCritical,
    bool AllFramesRecovered,
    bool? CriticalLossWithinThreshold,
    long BRefBits,
    long BSentBits,
    double ComputeSeconds,
    int PacketsAttempted,
    int PacketsTransmitted,
    int PacketsOverBudget,
    int PacketsDelivered,
    ChannelStats? Channel,
    QResult? Metric,
    string? Error);

/// <summary>Підсумок сценарію за всіма seed (пастка 12: Q — середнє за seed; незавершений прогін дає 0).</summary>
public sealed record ScenarioSummary(string Scenario, int Runs, int Completed, double MeanQ, double MeanR, double MinR);

/// <summary>Звіт повного прогону.</summary>
public sealed record RunReport(
    IReadOnlyList<int> Seeds,
    IReadOnlyList<RunResult> Runs,
    IReadOnlyList<ScenarioSummary> Scenarios,
    double ElapsedSeconds,
    double TimeLimitSeconds,
    bool TimeLimitExceeded)
{
    /// <summary>Усі прогони завершено (Ok) і ліміт часу дотримано.</summary>
    public bool AllOk => !TimeLimitExceeded && Runs.All(r => r.Status == RunStatus.Ok);
}

/// <summary>Налаштування повного прогону.</summary>
/// <param name="Config">team_config.json.</param>
/// <param name="Scenarios">Сценарії в порядку прогону.</param>
/// <param name="Seeds">Seed каналу.</param>
/// <param name="OutputDirectory">Тека результатів (results.json, results.csv, recovered/).</param>
/// <param name="TimeLimitSeconds">Ліміт повного прогону; після нього решта прогонів пропускається.</param>
public sealed record RunSettings(
    TeamConfig Config,
    IReadOnlyList<string> Scenarios,
    IReadOnlyList<int> Seeds,
    string OutputDirectory,
    double TimeLimitSeconds);

/// <summary>
/// Повний прогін: кожен сценарій × кожен seed. Кожен виняток перехоплюється і пишеться в журнал (C2 п.3),
/// результати й відновлені кадри пишуться атомарно (C5 п.4), прогін вкладається в ліміт часу (C1 №12).
/// Власник: тімлід.
/// </summary>
/// <remarks>
/// Складові Q (README): B_ref = 96 · кадрів; B_sent — <see cref="RecordingChannel.SentBits"/>;
/// R — частка критичних кадрів входу, відновлених побайтово (без критичних кадрів R = 1);
/// t — час передавача і приймача без часу всередині каналу.
/// </remarks>
/// <param name="channelFactory">Канал для (сценарій, seed); якщо він <see cref="IDisposable"/>, звільняється.</param>
/// <param name="systemFactory">Нова система для кожного прогону.</param>
/// <param name="log">Журнал прогону.</param>
public sealed class SessionRunner(
    Func<string, int, IChannel> channelFactory,
    Func<ISessionSystem> systemFactory,
    RunLog log)
{
    /// <summary>Файл зведених результатів.</summary>
    public const string ResultsJson = "results.json";

    /// <summary>Таблиця результатів по прогонах.</summary>
    public const string ResultsCsv = "results.csv";

    /// <summary>Тека відновлених кадрів (по файлу на прогін).</summary>
    public const string RecoveredDirectory = "recovered";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        NumberHandling = global::System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals,
        Converters = { new global::System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    /// <summary>Виконує повний прогін і пише результати в <see cref="RunSettings.OutputDirectory"/>.</summary>
    /// <param name="loadFrames">Завантаження кадрів сеансу (один раз на прогін).</param>
    /// <param name="settings">Налаштування.</param>
    public RunReport Run(Func<IReadOnlyList<Frame>> loadFrames, RunSettings settings)
    {
        ArgumentNullException.ThrowIfNull(loadFrames);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(channelFactory);
        ArgumentNullException.ThrowIfNull(systemFactory);
        ArgumentNullException.ThrowIfNull(log);

        var total = Stopwatch.StartNew();
        log.Info(string.Create(
            CultureInfo.InvariantCulture,
            $"повний прогін: сценарії [{string.Join(", ", settings.Scenarios)}], seed [{string.Join(", ", settings.Seeds)}], ліміт {settings.TimeLimitSeconds} с"));

        Frame[]? frames = null;
        string? loadError = null;
        try
        {
            frames = [.. loadFrames()];
        }
        catch (Exception ex)
        {
            log.Error("завантаження кадрів сеансу", ex);
            loadError = Describe(ex);
        }

        var runs = new List<RunResult>();
        foreach (var scenario in settings.Scenarios)
        {
            foreach (var seed in settings.Seeds)
            {
                if (total.Elapsed.TotalSeconds > settings.TimeLimitSeconds)
                {
                    log.Warn(string.Create(CultureInfo.InvariantCulture, $"{scenario}/{seed}: пропущено, ліміт {settings.TimeLimitSeconds} с вичерпано"));
                    runs.Add(Empty(scenario, seed, RunStatus.Skipped, frames, "ліміт часу повного прогону вичерпано"));
                }
                else if (frames is null)
                {
                    runs.Add(Empty(scenario, seed, RunStatus.Failed, null, loadError));
                }
                else
                {
                    runs.Add(RunOne(scenario, seed, frames, settings));
                }
            }
        }

        total.Stop();
        var elapsed = total.Elapsed.TotalSeconds;
        var report = new RunReport(
            settings.Seeds,
            runs,
            [.. settings.Scenarios.Select(s => Summarize(s, runs))],
            elapsed,
            settings.TimeLimitSeconds,
            elapsed > settings.TimeLimitSeconds);

        foreach (var s in report.Scenarios)
        {
            log.Info(string.Create(
                CultureInfo.InvariantCulture,
                $"підсумок {s.Scenario}: завершено {s.Completed}/{s.Runs}, середнє Q = {s.MeanQ:F4}, середнє R = {s.MeanR:F4}, мін. R = {s.MinR:F4}"));
        }

        log.Info(string.Create(
            CultureInfo.InvariantCulture,
            $"повний прогін: {elapsed:F2} с із {settings.TimeLimitSeconds} с{(report.TimeLimitExceeded ? " — ЛІМІТ ПЕРЕВИЩЕНО" : "")}"));

        WriteResults(settings.OutputDirectory, report);
        return report;
    }

    /// <summary>R і повнота відновлення: збіг кадрів як мультимножин (побайтово, через рівність записів).</summary>
    public static (int Recovered, int RecoveredCritical, bool All) Match(IReadOnlyList<Frame> input, IReadOnlyList<Frame> output)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);
        var available = new Dictionary<Frame, int>();
        foreach (var f in output)
        {
            available[f] = available.GetValueOrDefault(f) + 1;
        }

        var recovered = 0;
        var critical = 0;
        foreach (var f in input)
        {
            if (available.GetValueOrDefault(f) > 0)
            {
                available[f]--;
                recovered++;
                if (f.IsCritical)
                {
                    critical++;
                }
            }
        }

        return (recovered, critical, recovered == input.Count && output.Count == input.Count);
    }

    private RunResult RunOne(string scenario, int seed, Frame[] frames, RunSettings settings)
    {
        log.Info(string.Create(CultureInfo.InvariantCulture, $"{scenario}/{seed}: старт, кадрів {frames.Length}"));
        var critical = frames.Count(f => f.IsCritical);
        var bRef = 8L * Frame.Size * frames.Length;
        IChannel? channel = null;
        RecordingChannel? recording = null;
        try
        {
            if (!settings.Config.Channel.Scenarios.TryGetValue(scenario, out var sc))
            {
                throw new KeyNotFoundException($"сценарій «{scenario}» відсутній у team_config.json");
            }

            var budget = sc.TransmissionBudgetSeconds ?? double.PositiveInfinity;
            channel = channelFactory(scenario, seed);
            recording = new RecordingChannel(channel, sc.BandwidthBps, budget);
            var system = systemFactory();

            var watch = Stopwatch.StartNew();
            system.Transmit([.. frames], recording, budget);

            IReadOnlyList<Frame> output;
            var crashed = false;
            try
            {
                output = system.Receive(recording.DeliveredInArrivalOrder());
            }
            catch (Exception ex)
            {
                log.Error($"{scenario}/{seed}: приймач", ex);
                output = [];
                crashed = true;
            }

            watch.Stop();
            var compute = Math.Max(0, (watch.Elapsed - recording.ChannelCallTime).TotalSeconds);

            var stats = recording.Stats();
            if (stats.Sent != recording.Transmitted)
            {
                log.Warn(string.Create(
                    CultureInfo.InvariantCulture,
                    $"{scenario}/{seed}: облік бюджету розійшовся з каналом (надіслано {stats.Sent}, пораховано {recording.Transmitted})"));
            }

            var (recovered, recoveredCritical, all) = Match(frames, output);
            var r = critical == 0 ? 1.0 : (double)recoveredCritical / critical;
            var q = Metric.Compute(settings.Config.Metric, bRef, recording.SentBits, r, compute, crashed);
            bool? withinThreshold = settings.Config.CriticalLossThresholds.TryGetValue(scenario, out var threshold)
                ? 1 - r <= threshold + 1e-12
                : null;

            SessionCsv.Save(RecoveredPath(settings.OutputDirectory, scenario, seed), output);
            var result = new RunResult(
                scenario, seed, crashed ? RunStatus.ReceiverCrashed : RunStatus.Ok,
                frames.Length, critical, recovered, recoveredCritical, all, withinThreshold,
                bRef, recording.SentBits, compute,
                recording.Attempted, recording.Transmitted, recording.OverBudget, recording.Delivered.Count,
                stats, q, crashed ? "приймач аварійно завершився (деталі в журналі)" : null);
            log.Info(string.Create(
                CultureInfo.InvariantCulture,
                $"{scenario}/{seed}: {result.Status}, Q = {q.Q:F4} (S = {q.S:F4}, R = {q.R:F4}, T = {q.T:F4}), пакетів {recording.Attempted}, у бюджеті {recording.Transmitted}, доставлено {recording.Delivered.Count}"));
            return result;
        }
        catch (Exception ex)
        {
            log.Error($"{scenario}/{seed}: прогін", ex);
            return new RunResult(
                scenario, seed, RunStatus.Failed, frames.Length, critical, 0, 0, false, null, bRef,
                recording?.SentBits ?? 0, 0, recording?.Attempted ?? 0, recording?.Transmitted ?? 0,
                recording?.OverBudget ?? 0, recording?.Delivered.Count ?? 0, null, null, Describe(ex));
        }
        finally
        {
            DisposeChannel(channel, scenario, seed);
        }
    }

    private void DisposeChannel(IChannel? channel, string scenario, int seed)
    {
        try
        {
            (channel as IDisposable)?.Dispose();
        }
        catch (Exception ex)
        {
            log.Error($"{scenario}/{seed}: закриття каналу", ex);
        }
    }

    private static RunResult Empty(string scenario, int seed, RunStatus status, Frame[]? frames, string? error)
    {
        var count = frames?.Length ?? 0;
        return new RunResult(
            scenario, seed, status, count, frames?.Count(f => f.IsCritical) ?? 0, 0, 0, false, null,
            8L * Frame.Size * count, 0, 0, 0, 0, 0, 0, null, null, error);
    }

    private static ScenarioSummary Summarize(string scenario, List<RunResult> runs)
    {
        var mine = runs.Where(r => r.Scenario == scenario).ToList();
        var qs = mine.Select(r => r.Metric?.Q ?? 0).ToList();
        var rs = mine.Select(r => r.Metric?.R ?? 0).ToList();
        return new ScenarioSummary(
            scenario,
            mine.Count,
            mine.Count(r => r.Status == RunStatus.Ok),
            qs.Count > 0 ? qs.Average() : 0,
            rs.Count > 0 ? rs.Average() : 0,
            rs.Count > 0 ? rs.Min() : 0);
    }

    private static string RecoveredPath(string outputDirectory, string scenario, int seed) =>
        Path.Combine(outputDirectory, RecoveredDirectory, string.Create(CultureInfo.InvariantCulture, $"{scenario}_{seed}.csv"));

    private static void WriteResults(string outputDirectory, RunReport report)
    {
        AtomicFile.WriteAllText(Path.Combine(outputDirectory, ResultsJson), JsonSerializer.Serialize(report, JsonOptions) + "\n");

        var csv = new StringBuilder();
        csv.Append("scenario,seed,status,frames,critical,recovered,recovered_critical,all_recovered,critical_loss_ok,")
            .Append("b_ref_bits,b_sent_bits,compute_s,packets,transmitted,over_budget,delivered,S,R,T,Q,error\n");
        foreach (var r in report.Runs)
        {
            csv.Append(CultureInfo.InvariantCulture, $"{r.Scenario},{r.Seed},{r.Status},{r.Frames},{r.CriticalFrames},")
                .Append(CultureInfo.InvariantCulture, $"{r.RecoveredFrames},{r.RecoveredCritical},{r.AllFramesRecovered},")
                .Append(CultureInfo.InvariantCulture, $"{r.CriticalLossWithinThreshold?.ToString() ?? ""},{r.BRefBits},{r.BSentBits},")
                .Append(CultureInfo.InvariantCulture, $"{r.ComputeSeconds:F4},{r.PacketsAttempted},{r.PacketsTransmitted},")
                .Append(CultureInfo.InvariantCulture, $"{r.PacketsOverBudget},{r.PacketsDelivered},")
                .Append(CultureInfo.InvariantCulture, $"{Num(r.Metric?.S)},{Num(r.Metric?.R)},{Num(r.Metric?.T)},{Num(r.Metric?.Q)},")
                .Append(CsvField(r.Error))
                .Append('\n');
        }

        AtomicFile.WriteAllText(Path.Combine(outputDirectory, ResultsCsv), csv.ToString());
    }

    private static string Num(double? value) =>
        value is { } v ? v.ToString("F6", CultureInfo.InvariantCulture) : "";

    private static string CsvField(string? value) =>
        value is null ? "" : "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal).ReplaceLineEndings(" ") + "\"";

    private string Describe(Exception ex) => log.Redact(ex.GetType().Name + ": " + ex.Message);
}
