using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Telemetry.System;
using Telemetry.Tests.Infrastructure;

namespace Telemetry.Tests.SystemZone;

/// <summary>Пастка 11: ChannelBridge дає рівно ті самі рішення, що й прямий запуск channel.py.</summary>
public class ChannelBridgeTests
{
    private const int OpenSeed = 425243;

    private static string Config => TestData.Path("data", "team_config.json");

    private static string Bridge => TestData.Path("channel", "bridge.py");

    /// <summary>Детерміновані розміри пакетів 75…512 Б, щоб зачепити і бюджет, і пачкові втрати.</summary>
    private static int[] PacketSizes(int n)
    {
        var rng = new Random(7);
        return Enumerable.Range(0, n).Select(_ => rng.Next(75, 513)).ToArray();
    }

    /// <summary>Прямий запуск channel.py без мосту: список (delivered, arrival) і stats().</summary>
    private static (List<(bool, double?)> Results, JsonElement Stats) RunDirect(string scenario, int seed, int[] sizes)
    {
        const string script = """
            import json, sys
            sys.path.insert(0, sys.argv[1])
            from channel import Channel
            req = json.load(sys.stdin)
            ch = Channel(req["scenario"], req["seed"])
            out = [list(ch.send(bytes(n))) for n in req["sizes"]]
            print(json.dumps({"results": out, "stats": ch.stats()}, ensure_ascii=False))
            """;
        var psi = new ProcessStartInfo(ChannelBridge.DefaultPython())
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            StandardOutputEncoding = new global::System.Text.UTF8Encoding(false),
        };
        psi.ArgumentList.Add("-I");
        psi.ArgumentList.Add("-X");
        psi.ArgumentList.Add("utf8");
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add(script);
        psi.ArgumentList.Add(TestData.Path("channel"));
        using var p = Process.Start(psi)!;
        var sizesJson = string.Join(",", sizes.Select(s => s.ToString(CultureInfo.InvariantCulture)));
        p.StandardInput.Write($"{{\"scenario\":{scenario},\"seed\":{seed},\"sizes\":[{sizesJson}]}}");
        p.StandardInput.Close();
        var output = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        Assert.Equal(0, p.ExitCode);

        var root = JsonDocument.Parse(output).RootElement;
        var results = root.GetProperty("results").EnumerateArray()
            .Select(r => (r[0].GetBoolean(), r[1].ValueKind == JsonValueKind.Null ? (double?)null : r[1].GetDouble()))
            .ToList();
        return (results, root.GetProperty("stats").Clone());
    }

    [Theory]
    [InlineData("спокійний", OpenSeed)]
    [InlineData("робочий", OpenSeed)]
    [InlineData("аварійний", OpenSeed)]
    [InlineData("аварійний", OpenSeed + 1)]
    public void Bridge_matches_direct_channel_py_on_same_seed(string scenarioName, int seed)
    {
        var scenario = ChannelBridge.ScenarioJson(File.ReadAllText(Config), scenarioName);
        var sizes = PacketSizes(300);
        var (expected, expectedStats) = RunDirect(scenario, seed, sizes);

        using var bridge = new ChannelBridge(scenario, seed, Bridge);
        var actual = sizes.Select(n => bridge.Send(new byte[n])).ToList();

        Assert.Equal(expected, actual);
        var stats = bridge.Stats();
        Assert.Equal(expectedStats.GetProperty("надіслано").GetInt32(), stats.Sent);
        Assert.Equal(expectedStats.GetProperty("втрачено").GetInt32(), stats.Lost);
        Assert.Equal(expectedStats.GetProperty("не_вміщено_в_бюджет").GetInt32(), stats.OverBudget);
        Assert.Equal(expectedStats.GetProperty("час_каналу_с").GetDouble(), stats.ChannelTimeSeconds);
    }

    [Fact]
    public void Emergency_scenario_has_losses_and_budget_overflow()
    {
        using var bridge = ChannelBridge.FromConfig(Config, "аварійний", OpenSeed, Bridge);
        foreach (var n in PacketSizes(300))
        {
            bridge.Send(new byte[n]);
        }

        var s = bridge.Stats();
        Assert.True(s.Lost > 0);
        Assert.True(s.OverBudget > 0);
        Assert.Equal(16.9, s.BudgetSeconds);
        Assert.True(s.ChannelTimeSeconds <= s.BudgetSeconds);
        Assert.Equal(300, s.Sent + s.OverBudget);
    }

    [Fact]
    public void Calm_scenario_delivers_everything_with_constant_delay()
    {
        using var bridge = ChannelBridge.FromConfig(Config, "спокійний", OpenSeed, Bridge);
        var (delivered, arrival) = bridge.Send(new byte[100]);
        Assert.True(delivered);
        Assert.Equal((800.0 / 64000) + 0.020, arrival!.Value, 12);
        Assert.Equal(0, bridge.Stats().Lost);
    }

    [Fact]
    public void Same_seed_is_reproducible()
    {
        var sizes = PacketSizes(100);
        List<(bool, double?)> Run()
        {
            using var b = ChannelBridge.FromConfig(Config, "аварійний", OpenSeed, Bridge);
            return sizes.Select(n => b.Send(new byte[n])).ToList();
        }

        Assert.Equal(Run(), Run());
    }

    [Fact]
    public void Unknown_scenario_is_reported() =>
        Assert.Throws<KeyNotFoundException>(() => ChannelBridge.ScenarioJson(File.ReadAllText(Config), "немає"));

    [Fact]
    public void Invalid_scenario_json_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => new ChannelBridge("[1,2]", 1, Bridge));
        Assert.Throws<ArgumentException>(() => new ChannelBridge(" ", 1, Bridge));
    }

    [Fact]
    public void Bad_scenario_fields_surface_as_bridge_error()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => new ChannelBridge("{\"x\":1}", 1, Bridge));
        Assert.Contains("bridge.py", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Missing_bridge_script_fails_with_clear_error()
    {
        var scenario = ChannelBridge.ScenarioJson(File.ReadAllText(Config), "спокійний");
        Assert.Throws<InvalidOperationException>(() => new ChannelBridge(scenario, 1, TestData.Path("channel", "немає.py")));
    }

    [Fact]
    public void Dispose_is_idempotent_and_blocks_further_use()
    {
        var bridge = ChannelBridge.FromConfig(Config, "спокійний", OpenSeed, Bridge);
        bridge.Dispose();
        bridge.Dispose();
        Assert.Throws<ObjectDisposedException>(() => bridge.Send([1]));
    }

    [Fact]
    public void Bridge_script_is_found_from_test_directory() =>
        Assert.EndsWith(global::System.IO.Path.Combine("channel", "bridge.py"), ChannelBridge.FindBridgeScript(), StringComparison.Ordinal);

    [Fact]
    public void Default_python_respects_environment_variable()
    {
        var old = Environment.GetEnvironmentVariable("TELEMETRY_PYTHON");
        try
        {
            Environment.SetEnvironmentVariable("TELEMETRY_PYTHON", "my-python");
            Assert.Equal("my-python", ChannelBridge.DefaultPython());
            Environment.SetEnvironmentVariable("TELEMETRY_PYTHON", null);
            Assert.Equal(OperatingSystem.IsWindows() ? "python" : "python3", ChannelBridge.DefaultPython());
        }
        finally
        {
            Environment.SetEnvironmentVariable("TELEMETRY_PYTHON", old);
        }
    }
}
