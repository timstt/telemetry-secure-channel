using System.Text.Json;
using System.Text.Json.Serialization;

namespace Telemetry.Core;

/// <summary>
/// Типізоване читання data/team_config.json. Назви полів у JSON — українською,
/// тому кожна властивість має явний <see cref="JsonPropertyNameAttribute"/>.
/// Власник: тімлід.
/// </summary>
public sealed class TeamConfig
{
    /// <summary>Ідентифікатор команди (T01).</summary>
    [JsonPropertyName("команда")] public string Team { get; init; } = "";

    /// <summary>Seed команди.</summary>
    [JsonPropertyName("seed")] public int Seed { get; init; }

    /// <summary>Версія формату пакета.</summary>
    [JsonPropertyName("версія_формату")] public int FormatVersion { get; init; }

    /// <summary>Пароль сеансу. НІКОЛИ не логувати (C5 п.3).</summary>
    [JsonPropertyName("пароль_сеансу")] public string SessionPassword { get; init; } = "";

    /// <summary>Коефіцієнти метрики Q.</summary>
    [JsonPropertyName("метрика_Q")] public MetricConfig Metric { get; init; } = new();

    /// <summary>Параметри пакування.</summary>
    [JsonPropertyName("пакування")] public PackingConfig Packing { get; init; } = new();

    /// <summary>Пріоритети кадрів за класом.</summary>
    [JsonPropertyName("пріоритети_кадрів")] public PrioritiesConfig Priorities { get; init; } = new();

    /// <summary>Канал і сценарії.</summary>
    [JsonPropertyName("канал")] public ChannelConfig Channel { get; init; } = new();

    /// <summary>Допустима частка втрачених критичних кадрів за сценарієм.</summary>
    [JsonPropertyName("пороги_втрати_критичних_кадрів")]
    public Dictionary<string, double> CriticalLossThresholds { get; init; } = new();

    /// <summary>Ліміт часу повного прогону, с.</summary>
    [JsonPropertyName("ліміт_часу_повного_прогону_секунд")] public double FullRunLimitSeconds { get; init; }

    /// <summary>Параметри перцептрона.</summary>
    [JsonPropertyName("перцептрон")] public PerceptronConfig Perceptron { get; init; } = new();

    private static readonly JsonSerializerOptions Options = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Розбирає JSON-текст конфігурації.</summary>
    /// <exception cref="InvalidDataException">JSON порожній або не відповідає схемі.</exception>
    public static TeamConfig Parse(string json)
    {
        TeamConfig? cfg;
        try
        {
            cfg = JsonSerializer.Deserialize<TeamConfig>(json, Options);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("team_config.json: некоректний JSON", ex);
        }

        if (cfg is null)
        {
            throw new InvalidDataException("team_config.json: порожній документ");
        }

        cfg.Validate();
        return cfg;
    }

    /// <summary>Читає файл конфігурації.</summary>
    public static TeamConfig Load(string path) => Parse(File.ReadAllText(path));

    /// <summary>Перевіряє інваріанти, від яких залежать інші модулі.</summary>
    /// <exception cref="InvalidDataException">Порушено інваріант.</exception>
    public void Validate()
    {
        Require(Packing.MaxPacketBytes > PacketPlan.Overhead, "max_packet_bytes має бути > 75");
        Require(
            Packing.MaxPayloadBytes == Packing.MaxPacketBytes - PacketPlan.Overhead,
            "max_payload_bytes ≠ max_packet_bytes − 43 − 32");
        Require(Packing.CandidateWindowFrames > 0, "candidate_window_frames має бути > 0");
        Require(Math.Abs(Metric.Alpha + Metric.Beta + Metric.Gamma - 1.0) < 1e-9, "alpha + beta + gamma ≠ 1");
        Require(Metric.TRefSeconds > 0, "t_ref має бути > 0");
        Require(Channel.Scenarios.Count > 0, "немає сценаріїв каналу");
        foreach (var (name, sc) in Channel.Scenarios)
        {
            Require(sc.BandwidthBps > 0, $"сценарій {name}: bandwidth_bps має бути > 0");
            Require(sc.LossProbability is >= 0 and < 1, $"сценарій {name}: loss_probability поза [0,1)");
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidDataException("team_config.json: " + message);
        }
    }
}

/// <summary>Секція «метрика_Q».</summary>
public sealed class MetricConfig
{
    /// <summary>Вага S.</summary>
    [JsonPropertyName("alpha")] public double Alpha { get; init; }

    /// <summary>Вага R.</summary>
    [JsonPropertyName("beta")] public double Beta { get; init; }

    /// <summary>Вага T.</summary>
    [JsonPropertyName("gamma")] public double Gamma { get; init; }

    /// <summary>Еталонний час t_ref, с.</summary>
    [JsonPropertyName("t_ref_секунд")] public double TRefSeconds { get; init; }
}

/// <summary>Секція «пакування».</summary>
public sealed class PackingConfig
{
    /// <summary>Ліміт на весь пакет (заголовок + шифротекст + HMAC).</summary>
    [JsonPropertyName("max_packet_bytes")] public int MaxPacketBytes { get; init; }

    /// <summary>max_packet_bytes − 75.</summary>
    [JsonPropertyName("max_payload_bytes")] public int MaxPayloadBytes { get; init; }

    /// <summary>Розмір вікна кандидатів (64).</summary>
    [JsonPropertyName("candidate_window_frames")] public int CandidateWindowFrames { get; init; }

    /// <summary>CLASS_TABLE у задачі пакування (0).</summary>
    [JsonPropertyName("class_table_у_задачі_пакування")] public int ClassTableInPacking { get; init; }

    /// <summary>Місткість рюкзака W = 8·(max_payload − 14 − 2·table_nsym), біт.</summary>
    public int CapacityBits(int tableNsym) => 8 * (MaxPayloadBytes - 14 - (2 * tableNsym));
}

/// <summary>Секція «пріоритети_кадрів».</summary>
public sealed class PrioritiesConfig
{
    /// <summary>Пріоритет класу 0.</summary>
    [JsonPropertyName("норма")] public int Normal { get; init; }

    /// <summary>Пріоритет класу 2.</summary>
    [JsonPropertyName("перехідний_режим")] public int Transitional { get; init; }

    /// <summary>Пріоритет класу 1.</summary>
    [JsonPropertyName("аномалія")] public int Anomaly { get; init; }

    /// <summary>Бонус за critical = 1.</summary>
    [JsonPropertyName("critical_бонус")] public int CriticalBonus { get; init; }

    /// <summary>Пріоритет за класом перцептрона (0 норма, 1 аномалія, 2 перехідний).</summary>
    /// <exception cref="ArgumentOutOfRangeException">Невідомий клас.</exception>
    public int ForClass(int cls) => cls switch
    {
        0 => Normal,
        1 => Anomaly,
        2 => Transitional,
        _ => throw new ArgumentOutOfRangeException(nameof(cls), cls, "клас має бути 0, 1 або 2"),
    };

    /// <summary>v_i = пріоритет(клас) + critical_бонус·[critical].</summary>
    public int Value(int cls, bool critical) => ForClass(cls) + (critical ? CriticalBonus : 0);
}

/// <summary>Секція «канал».</summary>
public sealed class ChannelConfig
{
    /// <summary>Відкритий seed (лише налагодження).</summary>
    [JsonPropertyName("seed_моделі_каналу")] public int Seed { get; init; }

    /// <summary>Сценарії: спокійний / робочий / аварійний.</summary>
    [JsonPropertyName("сценарії")] public Dictionary<string, ScenarioConfig> Scenarios { get; init; } = new();
}

/// <summary>
/// Один сценарій каналу. Для channel.py ChannelBridge передає сирий JSON сценарію з файлу
/// (через JsonDocument), щоб не втратити жодного поля.
/// </summary>
public sealed class ScenarioConfig
{
    /// <summary>Цільова частка втрат.</summary>
    [JsonPropertyName("loss_probability")] public double LossProbability { get; init; }

    /// <summary>Пропускна здатність, біт/с.</summary>
    [JsonPropertyName("bandwidth_bps")] public double BandwidthBps { get; init; }

    /// <summary>Бюджет часу передавання, с (null — без обмеження).</summary>
    [JsonPropertyName("transmission_budget_s")] public double? TransmissionBudgetSeconds { get; init; }

    /// <summary>Закон затримки.</summary>
    [JsonPropertyName("delay_ms")] public DelayConfig Delay { get; init; } = new();

    /// <summary>Пачкові втрати (може бути відсутнім → B = 1).</summary>
    [JsonPropertyName("втрати_пачками")] public BurstConfig? Burst { get; init; }

    /// <summary>Середня довжина пачки B.</summary>
    public double MeanBurstLength => Burst?.MeanLength ?? 1.0;
}

/// <summary>Закон затримки каналу.</summary>
public sealed class DelayConfig
{
    /// <summary>«стала» або «рівномірна».</summary>
    [JsonPropertyName("тип")] public string Kind { get; init; } = "";

    /// <summary>Значення для сталої затримки, мс.</summary>
    [JsonPropertyName("значення")] public double? Value { get; init; }

    /// <summary>Нижня межа рівномірної затримки, мс.</summary>
    [JsonPropertyName("від")] public double? From { get; init; }

    /// <summary>Верхня межа рівномірної затримки, мс.</summary>
    [JsonPropertyName("до")] public double? To { get; init; }
}

/// <summary>Параметри пачкових втрат.</summary>
public sealed class BurstConfig
{
    /// <summary>Середня довжина пачки B.</summary>
    [JsonPropertyName("середня_довжина_пачки")] public double MeanLength { get; init; } = 1.0;
}

/// <summary>Секція «перцептрон».</summary>
public sealed class PerceptronConfig
{
    /// <summary>Мінімальний macro-F1 на holdout.</summary>
    [JsonPropertyName("F1_min")] public double F1Min { get; init; }

    /// <summary>macro-F1 мажоритарного класу (довідково).</summary>
    [JsonPropertyName("базова_лінія_мажоритарний_клас")] public double MajorityBaseline { get; init; }
}
