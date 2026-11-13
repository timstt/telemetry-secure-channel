using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Telemetry.Core;

namespace Telemetry.System;

/// <summary>
/// Міст до НЕЗМІНЕНОГО channel/channel.py: один дочірній процес <c>python channel/bridge.py</c> на прогін,
/// протокол — рядок JSON у stdin/stdout (UTF-8):
/// <c>{"init":{"scenario":{…},"seed":N}}</c>, <c>{"send":"&lt;hex&gt;"}</c>, <c>{"stats":1}</c>.
/// Канал не переписується на C#: послідовність втрат залежить від <c>random.Random</c> Python.
/// Власник: тімлід.
/// </summary>
/// <remarks>
/// Інтерпретатор Python: змінна середовища <c>TELEMETRY_PYTHON</c>, інакше <c>python</c> на Windows
/// і <c>python3</c> на інших ОС. Вміст пакетів і ключі в лог не потрапляють (C5 п.3).
/// </remarks>
public sealed class ChannelBridge : IChannel, IDisposable
{
    private readonly Process _process;
    private bool _disposed;

    /// <summary>Запускає міст для сценарію (сирий JSON сценарію з team_config) і seed.</summary>
    /// <param name="scenarioJson">JSON-об'єкт сценарію рівно як у team_config.json.</param>
    /// <param name="seed">Seed моделі каналу.</param>
    /// <param name="bridgePath">Шлях до bridge.py; за замовчуванням шукається channel/bridge.py вгору від теки програми.</param>
    /// <param name="pythonExecutable">Інтерпретатор Python; за замовчуванням див. <see cref="DefaultPython"/>.</param>
    /// <exception cref="InvalidOperationException">Міст не запустився або не підтвердив ініціалізацію.</exception>
    public ChannelBridge(string scenarioJson, int seed, string? bridgePath = null, string? pythonExecutable = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scenarioJson);
        var scenario = JsonNode.Parse(scenarioJson) as JsonObject
            ?? throw new ArgumentException("сценарій має бути JSON-об'єктом", nameof(scenarioJson));

        var script = bridgePath ?? FindBridgeScript();
        var psi = new ProcessStartInfo(pythonExecutable ?? DefaultPython())
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
            StandardInputEncoding = new UTF8Encoding(false),
        };
        psi.ArgumentList.Add("-I");
        psi.ArgumentList.Add(script);

        _process = Process.Start(psi) ?? throw new InvalidOperationException("не вдалося запустити bridge.py");
        _process.StandardInput.AutoFlush = true;
        _process.StandardInput.NewLine = "\n";

        var init = new JsonObject
        {
            ["init"] = new JsonObject { ["scenario"] = scenario, ["seed"] = seed },
        };
        using var reply = Call(init.ToJsonString());
        if (!reply.RootElement.TryGetProperty("ok", out var ok) || !ok.GetBoolean())
        {
            throw new InvalidOperationException("bridge.py не підтвердив ініціалізацію каналу");
        }
    }

    /// <summary>Створює міст для сценарію з файла team_config.json.</summary>
    /// <param name="configPath">Шлях до team_config.json.</param>
    /// <param name="scenarioName">«спокійний», «робочий» або «аварійний».</param>
    /// <param name="seed">Seed каналу.</param>
    /// <param name="bridgePath">Необов'язковий шлях до bridge.py.</param>
    public static ChannelBridge FromConfig(string configPath, string scenarioName, int seed, string? bridgePath = null) =>
        new(ScenarioJson(File.ReadAllText(configPath), scenarioName), seed, bridgePath);

    /// <summary>Витягає сирий JSON сценарію з тексту team_config.json, без втрати жодного поля.</summary>
    /// <exception cref="KeyNotFoundException">Сценарію немає в конфігурації.</exception>
    public static string ScenarioJson(string teamConfigJson, string scenarioName)
    {
        using var doc = JsonDocument.Parse(teamConfigJson);
        var scenarios = doc.RootElement.GetProperty("канал").GetProperty("сценарії");
        return scenarios.TryGetProperty(scenarioName, out var sc)
            ? sc.GetRawText()
            : throw new KeyNotFoundException($"сценарій «{scenarioName}» відсутній у team_config.json");
    }

    /// <summary>Інтерпретатор за замовчуванням: TELEMETRY_PYTHON або python/python3 залежно від ОС.</summary>
    public static string DefaultPython()
    {
        var env = Environment.GetEnvironmentVariable("TELEMETRY_PYTHON");
        if (!string.IsNullOrWhiteSpace(env))
        {
            return env;
        }

        return OperatingSystem.IsWindows() ? "python" : "python3";
    }

    /// <summary>Шукає channel/bridge.py у теці програми і вище (до кореня репозиторію).</summary>
    /// <exception cref="FileNotFoundException">Файл не знайдено.</exception>
    public static string FindBridgeScript()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "channel", "bridge.py");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException("не знайдено channel/bridge.py вище теки програми");
    }

    /// <inheritdoc/>
    public (bool Delivered, double? Arrival) Send(byte[] packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        var request = new JsonObject { ["send"] = Convert.ToHexString(packet) };
        using var reply = Call(request.ToJsonString());
        var root = reply.RootElement;
        var delivered = root.GetProperty("delivered").GetBoolean();
        var arrival = root.GetProperty("arrival");
        return (delivered, arrival.ValueKind == JsonValueKind.Null ? null : arrival.GetDouble());
    }

    /// <inheritdoc/>
    public ChannelStats Stats()
    {
        using var reply = Call("{\"stats\":1}");
        var r = reply.RootElement;
        return new ChannelStats(
            r.GetProperty("надіслано").GetInt32(),
            r.GetProperty("втрачено").GetInt32(),
            r.GetProperty("не_вміщено_в_бюджет").GetInt32(),
            r.GetProperty("частка_втрат").GetDouble(),
            r.GetProperty("частка_недоставлених").GetDouble(),
            r.GetProperty("час_каналу_с").GetDouble(),
            ParseBudget(r.GetProperty("бюджет_с")));
    }

    /// <summary>Закриває stdin мосту і чекає завершення процесу.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            _process.StandardInput.Close();
            if (!_process.WaitForExit(5000))
            {
                _process.Kill(entireProcessTree: true);
            }
        }
        finally
        {
            _process.Dispose();
        }
    }

    private static double ParseBudget(JsonElement e) =>
        e.ValueKind == JsonValueKind.Number
            ? e.GetDouble()
            : double.Parse(e.GetString() ?? "Infinity", CultureInfo.InvariantCulture);

    private JsonDocument Call(string requestJson)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _process.StandardInput.WriteLine(requestJson);
        var line = _process.StandardOutput.ReadLine();
        if (line is null)
        {
            var stderr = _process.StandardError.ReadToEnd();
            throw new InvalidOperationException("bridge.py завершився без відповіді: " + FirstLine(stderr));
        }

        var doc = JsonDocument.Parse(line);
        if (doc.RootElement.TryGetProperty("error", out var error))
        {
            var message = error.GetString();
            doc.Dispose();
            throw new InvalidOperationException("bridge.py: " + message);
        }

        return doc;
    }

    private static string FirstLine(string text)
    {
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return lines.Length > 0 ? lines[^1] : "(stderr порожній)";
    }
}
