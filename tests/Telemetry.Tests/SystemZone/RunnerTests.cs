using System.Buffers.Binary;
using System.Globalization;
using System.Text.Json;
using Telemetry.Core;
using Telemetry.System;
using Telemetry.Tests.Infrastructure;

namespace Telemetry.Tests.SystemZone;

/// <summary>Runner: атомарний запис (C5 п.4), журнал (C2 п.3, C5 п.3), seed (пастка 12), облік B_sent, прогін 3×5.</summary>
public sealed class RunnerTests : IDisposable
{
    private const string Password = "algo2026";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "telemetry-runner-" + Guid.NewGuid().ToString("N"));

    public RunnerTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static TeamConfig Config => TeamConfig.Load(TestData.Path("data", "team_config.json"));

    // ---------- AtomicFile ----------

    [Fact]
    public void Atomic_write_replaces_file_and_leaves_no_tmp()
    {
        var path = Path.Combine(_dir, "sub", "r.txt");
        AtomicFile.WriteAllText(path, "перший");
        AtomicFile.WriteAllText(path, "другий");
        Assert.Equal("другий", File.ReadAllText(path));
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void Atomic_write_failure_midway_keeps_previous_file()
    {
        var path = Path.Combine(_dir, "r.txt");
        AtomicFile.WriteAllText(path, "коректний результат");

        Assert.Throws<InvalidOperationException>(() => AtomicFile.Write(path, stream =>
        {
            stream.Write("частковий"u8);
            throw new InvalidOperationException("збій посередині запису");
        }));

        Assert.Equal("коректний результат", File.ReadAllText(path));
        Assert.False(File.Exists(path + ".tmp"));
    }

    // ---------- RunLog ----------

    [Fact]
    public void Log_masks_password_and_records_exception_type_and_stack()
    {
        using var text = new StringWriter();
        using var log = new RunLog(text, [Password, ""]);
        log.Info("пароль " + Password);
        try
        {
            throw new InvalidDataException("погано: " + Password);
        }
        catch (InvalidDataException ex)
        {
            log.Error("контекст", ex);
        }

        var content = text.ToString();
        Assert.DoesNotContain(Password, content, StringComparison.Ordinal);
        Assert.Contains(RunLog.Mask, content, StringComparison.Ordinal);
        Assert.Contains("ERROR контекст: System.IO.InvalidDataException", content, StringComparison.Ordinal);
        Assert.Contains(nameof(Log_masks_password_and_records_exception_type_and_stack), content, StringComparison.Ordinal);
        Assert.DoesNotContain("Unhandled", content, StringComparison.Ordinal);
        Assert.Equal(1, log.ErrorCount);

        log.Unhandled(new InvalidOperationException("x"));
        Assert.Contains("Unhandled", text.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void File_log_is_written_and_disposed_once()
    {
        var path = Path.Combine(_dir, "logs", "runner.log");
        var log = new RunLog(path, [Password]);
        log.Warn("попередження");
        log.Dispose();
        log.Dispose();
        Assert.Contains("WARN попередження", File.ReadAllText(path), StringComparison.Ordinal);
        Assert.Throws<ObjectDisposedException>(() => log.Info("після закриття"));
    }

    // ---------- RunSeeds ----------

    [Fact]
    public void Default_seeds_are_open_seed_and_four_derived()
    {
        var seeds = RunSeeds.Default(425243);
        Assert.Equal(new[] { 425243, 425244, 425245, 425246, 425247 }, seeds);
        Assert.Equal(RunSeeds.DefaultCount, seeds.Distinct().Count());
    }

    [Fact]
    public void Seed_list_is_parsed_and_validated()
    {
        Assert.Equal(new[] { 7, 8, 9 }, RunSeeds.Parse(" 7, 8 ,9 "));
        Assert.Throws<FormatException>(() => RunSeeds.Parse("1,x"));
        Assert.Throws<FormatException>(() => RunSeeds.Parse("1,1"));
        Assert.Throws<FormatException>(() => RunSeeds.Parse(" , "));
    }

    // ---------- RecordingChannel ----------

    [Fact]
    public void Recording_channel_counts_b_sent_like_channel_py_budget()
    {
        // 1000 біт/с, бюджет 2 с: 100 Б (0.8 с) так, 200 Б (1.6 с) ні, 100 Б (ще 0.8 с → 1.6) так, 75 Б (0.6) ні.
        var inner = new FakeChannel(lossEvery: 2);
        var rec = new RecordingChannel(inner, 1000, 2.0);
        rec.Send(new byte[100]);
        rec.Send(new byte[200]);
        rec.Send(new byte[100]);
        rec.Send(new byte[75]);

        Assert.Equal(4, rec.Attempted);
        Assert.Equal(2, rec.Transmitted);
        Assert.Equal(2, rec.OverBudget);
        Assert.Equal(8L * (100 + 100), rec.SentBits); // втрачені теж у B_sent, невміщені — ні
        Assert.Equal(4, rec.Stats().Sent);
        Assert.True(rec.ChannelCallTime >= TimeSpan.Zero);
        Assert.Throws<ArgumentOutOfRangeException>(() => new RecordingChannel(inner, 0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RecordingChannel(inner, 1, double.NaN));
    }

    [Fact]
    public void Delivered_packets_are_returned_in_arrival_order()
    {
        var inner = new FakeChannel(arrivals: [0.5, 0.1, 0.1]);
        var rec = new RecordingChannel(inner, 1e9, double.PositiveInfinity);
        rec.Send([1]);
        rec.Send([2]);
        rec.Send([3]);
        Assert.Equal(new byte[] { 2, 3, 1 }, rec.DeliveredInArrivalOrder().Select(p => p[0]));
    }

    [Fact]
    public void Recording_channel_matches_real_channel_py_budget_decisions()
    {
        var json = File.ReadAllText(TestData.Path("data", "team_config.json"));
        var sc = Config.Channel.Scenarios["аварійний"];
        using var bridge = new ChannelBridge(ChannelBridge.ScenarioJson(json, "аварійний"), 425243, TestData.Path("channel", "bridge.py"));
        var rec = new RecordingChannel(bridge, sc.BandwidthBps, sc.TransmissionBudgetSeconds!.Value);
        var rng = new Random(11);
        for (var i = 0; i < 260; i++)
        {
            rec.Send(new byte[rng.Next(75, 513)]);
        }

        var stats = rec.Stats();
        Assert.True(rec.OverBudget > 0);
        Assert.Equal(stats.Sent, rec.Transmitted);
        Assert.Equal(stats.OverBudget, rec.OverBudget);
        Assert.Equal(stats.Sent - stats.Lost, rec.Delivered.Count);
    }

    // ---------- SessionCsv ----------

    [Fact]
    public void Session_csv_skips_header_blank_and_invalid_lines_and_logs_them()
    {
        var path = Path.Combine(_dir, "s.csv");
        File.WriteAllText(path, "timestamp_ms,altitude_dm,temp_c10,vibration,mode,critical,label\n1,2,3,4,5,1,0\n\nbad\n6,7,8,9,1,0,2\n");
        using var text = new StringWriter();
        using var log = new RunLog(text, []);

        var frames = SessionCsv.Load(path, log, ParseLine);

        Assert.Equal(new[] { new Frame(1, 2, 3, 4, 5, 1), new Frame(6, 7, 8, 9, 1, 0) }, frames);
        Assert.Contains("рядок 4 пропущено", text.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Session_csv_round_trips_through_atomic_save()
    {
        var path = Path.Combine(_dir, "rec.csv");
        Frame[] frames = [new(10, -5, 300, -1, 7, 1), new(20, 0, 0, 0, 0, 0)];
        SessionCsv.Save(path, frames);
        using var log = new RunLog(new StringWriter(), []);
        Assert.Equal(frames, SessionCsv.Load(path, log, ParseLine));
    }

    // ---------- SessionRunner ----------

    [Fact]
    public void Match_counts_frames_as_multiset()
    {
        Frame a = new(1, 0, 0, 0, 0, 1), b = new(2, 0, 0, 0, 0, 0);
        Assert.Equal((2, 1, true), SessionRunner.Match([a, b], [b, a]));
        Assert.Equal((1, 1, false), SessionRunner.Match([a, a], [a]));
        Assert.Equal((2, 1, false), SessionRunner.Match([a, b], [a, b, b]));
    }

    [Fact]
    public void Full_run_3x5_writes_results_and_clean_log()
    {
        var frames = SampleFrames(60);
        var (report, logText) = Run(new LoopbackSystem(), lossEvery: 0, frames);

        Assert.Equal(15, report.Runs.Count);
        Assert.True(report.AllOk);
        Assert.All(report.Runs, r =>
        {
            Assert.Equal(RunStatus.Ok, r.Status);
            Assert.True(r.AllFramesRecovered);
            Assert.True(r.CriticalLossWithinThreshold);
            Assert.InRange(r.Metric!.Q, 0, 1);
            Assert.Equal(1.0, r.Metric.R);
            Assert.Equal(8L * 12 * 60, r.BRefBits);
            Assert.Equal(r.PacketsTransmitted * 8L * 87, r.BSentBits);
            Assert.True(File.Exists(Path.Combine(_dir, "out", "recovered", $"{r.Scenario}_{r.Seed}.csv")));
        });
        Assert.Equal(new[] { "спокійний", "робочий", "аварійний" }, report.Scenarios.Select(s => s.Scenario));

        var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(_dir, "out", SessionRunner.ResultsJson)));
        Assert.Equal(15, json.RootElement.GetProperty("Runs").GetArrayLength());
        Assert.Equal(16, File.ReadAllLines(Path.Combine(_dir, "out", SessionRunner.ResultsCsv)).Length);
        Assert.DoesNotContain("Unhandled", logText, StringComparison.Ordinal);
        Assert.DoesNotContain(Password, logText, StringComparison.Ordinal);
        Assert.DoesNotContain("ERROR", logText, StringComparison.Ordinal);
    }

    [Fact]
    public void Losses_lower_r_and_break_calm_threshold()
    {
        var (report, _) = Run(new LoopbackSystem(), lossEvery: 2, SampleFrames(40));
        var calm = report.Runs.First(r => r.Scenario == "спокійний");
        Assert.Equal(RunStatus.Ok, calm.Status);
        Assert.False(calm.AllFramesRecovered);
        Assert.False(calm.CriticalLossWithinThreshold);
        Assert.InRange(calm.Metric!.R, 0.01, 0.99);
        Assert.True(report.Scenarios.All(s => s.MinR < 1));
    }

    [Fact]
    public void Unimplemented_module_fails_runs_but_not_the_runner()
    {
        var (report, logText) = Run(new ThrowingSystem(onTransmit: true), lossEvery: 0, SampleFrames(10));
        Assert.All(report.Runs, r =>
        {
            Assert.Equal(RunStatus.Failed, r.Status);
            Assert.Null(r.Metric);
            Assert.Contains(nameof(NotImplementedException), r.Error, StringComparison.Ordinal);
        });
        Assert.False(report.AllOk);
        Assert.All(report.Scenarios, s => Assert.Equal(0, s.MeanQ));
        Assert.Contains("System.NotImplementedException", logText, StringComparison.Ordinal);
        Assert.DoesNotContain("Unhandled", logText, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(_dir, "out", SessionRunner.ResultsJson)));
    }

    [Fact]
    public void Receiver_crash_gives_q_zero()
    {
        var (report, logText) = Run(new ThrowingSystem(onTransmit: false), lossEvery: 0, SampleFrames(10));
        Assert.All(report.Runs, r =>
        {
            Assert.Equal(RunStatus.ReceiverCrashed, r.Status);
            Assert.Equal(0, r.Metric!.Q);
        });
        Assert.Contains("приймач", logText, StringComparison.Ordinal);
    }

    [Fact]
    public void Frame_loading_failure_marks_every_run_failed()
    {
        using var text = new StringWriter();
        using var log = new RunLog(text, [Password]);
        var runner = new SessionRunner((_, _) => new FakeChannel(), () => new LoopbackSystem(), log);
        var report = runner.Run(() => throw new InvalidDataException("файл сеансу " + Password), Settings(120));
        Assert.Equal(15, report.Runs.Count);
        Assert.All(report.Runs, r => Assert.Equal(RunStatus.Failed, r.Status));
        Assert.All(report.Runs, r => Assert.DoesNotContain(Password, r.Error, StringComparison.Ordinal));
        Assert.DoesNotContain(Password, text.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Unknown_scenario_and_channel_dispose_errors_are_logged()
    {
        using var text = new StringWriter();
        using var log = new RunLog(text, []);
        var runner = new SessionRunner((_, _) => new FakeChannel(throwOnDispose: true), () => new LoopbackSystem(), log);
        var settings = Settings(120) with { Scenarios = ["спокійний", "невідомий"], Seeds = [1] };
        var report = runner.Run(() => SampleFrames(5), settings);
        Assert.Equal(RunStatus.Ok, report.Runs[0].Status);
        Assert.Equal(RunStatus.Failed, report.Runs[1].Status);
        Assert.Contains("закриття каналу", text.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Exhausted_time_limit_skips_remaining_runs()
    {
        using var log = new RunLog(new StringWriter(), []);
        var runner = new SessionRunner((_, _) => new FakeChannel(), () => new LoopbackSystem(), log);
        var report = runner.Run(() => SampleFrames(5), Settings(-1));
        Assert.True(report.TimeLimitExceeded);
        Assert.False(report.AllOk);
        Assert.All(report.Runs, r => Assert.Equal(RunStatus.Skipped, r.Status));
    }

    [Fact]
    public void Progress_reports_each_completed_scenario_and_seed()
    {
        using var log = new RunLog(new StringWriter(), []);
        var runner = new SessionRunner((_, _) => new FakeChannel(), () => new LoopbackSystem(), log);
        var updates = new List<(RunResult Result, int Completed, int Total)>();
        var report = runner.Run(() => SampleFrames(5), Settings(120), (result, completed, total) => updates.Add((result, completed, total)));

        Assert.Equal(report.Runs, updates.Select(update => update.Result));
        Assert.Equal(Enumerable.Range(1, report.Runs.Count), updates.Select(update => update.Completed));
        Assert.All(updates, update => Assert.Equal(report.Runs.Count, update.Total));
    }

    [Fact]
    public void Real_team_system_never_crashes_the_runner()
    {
        // Працює і з заглушками зон (прогони Failed), і після інтеграції (Ok): Runner не падає, лог чистий.
        using var text = new StringWriter();
        using var log = new RunLog(text, [Password]);
        var runner = new SessionRunner((_, _) => new FakeChannel(), () => new TelemetrySystem(Config, new Scheduler()), log);
        var report = runner.Run(() => SampleFrames(30), Settings(120));
        Assert.Equal(15, report.Runs.Count);
        Assert.All(report.Runs, r => Assert.Contains(r.Status, new[] { RunStatus.Ok, RunStatus.Failed }));
        Assert.DoesNotContain("Unhandled", text.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(Password, text.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Full_run_through_real_channel_py_fits_time_limit()
    {
        // C1 №12 для обв'язки Runner: session.csv, 3 сценарії × 5 seed через справжній bridge.py.
        var json = File.ReadAllText(TestData.Path("data", "team_config.json"));
        var bridge = TestData.Path("channel", "bridge.py");
        using var text = new StringWriter();
        using var log = new RunLog(text, [Password]);
        var runner = new SessionRunner(
            (scenario, seed) => new ChannelBridge(ChannelBridge.ScenarioJson(json, scenario), seed, bridge),
            () => new LoopbackSystem(framesPerPacket: 30),
            log);
        var report = runner.Run(
            () => SessionCsv.Load(TestData.Path("data", "session.csv"), log, ParseLine),
            Settings(Config.FullRunLimitSeconds));

        Assert.True(report.AllOk, text.ToString());
        Assert.False(report.TimeLimitExceeded);
        Assert.All(report.Runs.Where(r => r.Scenario == "спокійний"), r => Assert.True(r.AllFramesRecovered));
        Assert.All(report.Runs, r => Assert.InRange(r.Metric!.Q, 0, 1));
        Assert.Contains(report.Runs, r => r.PacketsOverBudget > 0);
        Assert.DoesNotContain("Unhandled", text.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("WARN", text.ToString(), StringComparison.Ordinal);
    }

    // ---------- допоміжне ----------

    private RunSettings Settings(double limit) =>
        new(Config, ["спокійний", "робочий", "аварійний"], RunSeeds.Default(425243), Path.Combine(_dir, "out"), limit);

    private (RunReport Report, string Log) Run(ISessionSystem system, int lossEvery, IReadOnlyList<Frame> frames)
    {
        using var text = new StringWriter();
        RunReport report;
        using (var log = new RunLog(text, [Password]))
        {
            var runner = new SessionRunner((_, _) => new FakeChannel(lossEvery), () => system, log);
            report = runner.Run(() => frames, Settings(120));
        }

        return (report, text.ToString());
    }

    private static Frame[] SampleFrames(int n) =>
        [.. Enumerable.Range(0, n).Select(i => new Frame((uint)(i * 50), (short)(1000 + i), 230, (short)(i % 7), (byte)(i % 8), (byte)(i % 3 == 0 ? 1 : 0)))];

    private static Frame ParseLine(string line)
    {
        var p = line.Split(',');
        if (p.Length < 6)
        {
            throw new FormatException("мало полів");
        }

        var c = CultureInfo.InvariantCulture;
        return new Frame(uint.Parse(p[0], c), short.Parse(p[1], c), short.Parse(p[2], c), short.Parse(p[3], c), byte.Parse(p[4], c), byte.Parse(p[5], c));
    }

    private static byte[] Encode(IEnumerable<Frame> frames)
    {
        var list = frames.ToList();
        var bytes = new byte[PacketPlan.Overhead + (Frame.Size * list.Count)];
        for (var i = 0; i < list.Count; i++)
        {
            var s = bytes.AsSpan(PacketPlan.Overhead + (i * Frame.Size), Frame.Size);
            var f = list[i];
            BinaryPrimitives.WriteUInt32BigEndian(s, f.TimestampMs);
            BinaryPrimitives.WriteInt16BigEndian(s[4..], f.AltitudeDm);
            BinaryPrimitives.WriteInt16BigEndian(s[6..], f.TempC10);
            BinaryPrimitives.WriteInt16BigEndian(s[8..], f.Vibration);
            s[10] = f.Mode;
            s[11] = f.Critical;
        }

        return bytes;
    }

    private static IEnumerable<Frame> Decode(byte[] packet)
    {
        for (var o = PacketPlan.Overhead; o + Frame.Size <= packet.Length; o += Frame.Size)
        {
            var s = packet.AsSpan(o, Frame.Size);
            yield return new Frame(
                BinaryPrimitives.ReadUInt32BigEndian(s), BinaryPrimitives.ReadInt16BigEndian(s[4..]),
                BinaryPrimitives.ReadInt16BigEndian(s[6..]), BinaryPrimitives.ReadInt16BigEndian(s[8..]), s[10], s[11]);
        }
    }

    /// <summary>Система без стиснення і шифрування: «заголовок» 75 Б + сирі кадри; лише для обв'язки Runner.</summary>
    private sealed class LoopbackSystem(int framesPerPacket = 1) : ISessionSystem
    {
        public void Transmit(Frame[] frames, IChannel channel, double budgetSeconds)
        {
            foreach (var chunk in frames.Chunk(framesPerPacket))
            {
                channel.Send(Encode(chunk));
            }
        }

        public IReadOnlyList<Frame> Receive(IEnumerable<byte[]> packetsInArrivalOrder) =>
            [.. packetsInArrivalOrder.SelectMany(Decode).OrderBy(f => f.TimestampMs)];
    }

    private sealed class ThrowingSystem(bool onTransmit) : ISessionSystem
    {
        public void Transmit(Frame[] frames, IChannel channel, double budgetSeconds)
        {
            if (onTransmit)
            {
                throw new NotImplementedException();
            }

            channel.Send(Encode(frames));
        }

        public IReadOnlyList<Frame> Receive(IEnumerable<byte[]> packetsInArrivalOrder) =>
            throw new InvalidOperationException("приймач упав");
    }

    /// <summary>Канал без бюджету: кожен <paramref name="lossEvery"/>-й пакет губиться (0 — без втрат).</summary>
    private sealed class FakeChannel(int lossEvery = 0, double[]? arrivals = null, bool throwOnDispose = false) : IChannel, IDisposable
    {
        private int _sent;
        private int _lost;

        public (bool Delivered, double? Arrival) Send(byte[] packet)
        {
            _sent++;
            if (lossEvery > 0 && _sent % lossEvery == 0)
            {
                _lost++;
                return (false, null);
            }

            return (true, arrivals is null ? _sent : arrivals[_sent - 1]);
        }

        public ChannelStats Stats() => new(_sent, _lost, 0, 0, 0, 0, double.PositiveInfinity);

        public void Dispose()
        {
            if (throwOnDispose)
            {
                throw new IOException("міст не закрився");
            }
        }
    }
}
