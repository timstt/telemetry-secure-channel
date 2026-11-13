namespace Telemetry.Core;

// Контракти між модулями. Змінювати сигнатури — лише через issue тімліду.
// Усі цілі в пакетах — big-endian, біти — MSB-first (FORMAT_SPEC §0).

/// <summary>Канонічний Гаффман за FORMAT_SPEC §3.2–3.3. Власник: Саша.</summary>
public interface IHuffmanCodec
{
    /// <summary>
    /// Довжини кодів (індекс = символ 0…255, 0 — символ відсутній) за частотами.
    /// Обмеження 15 біт (length-limited), вироджений алфавіт дає length = 1.
    /// </summary>
    byte[] BuildLengths(long[] freq256);

    /// <summary>Кодує байти канонічними кодами; повертає потік і кількість значущих бітів.</summary>
    (byte[] Data, int Bits) Encode(byte[] src, byte[] lengths);

    /// <summary>Декодує рівно <paramref name="symbolCount"/> символів.</summary>
    byte[] Decode(byte[] data, byte[] lengths, int symbolCount);
}

/// <summary>Вибір кадрів у пакет (рюкзак 0/1, FORMAT_SPEC §3.5). ДП — Георгій, жадібний — Саша.</summary>
public interface IPacker
{
    /// <summary>Повертає індекси обраних предметів (за зростанням).</summary>
    int[] Select(int[] weightsBits, int[] values, int capacityBits);
}

/// <summary>Класифікатор кадрів (перцептрон). Власник: Ваня.</summary>
public interface IClassifier
{
    /// <summary>Клас 0/1/2 для кожного кадру послідовності, з урахуванням вікна історії.</summary>
    int[] Predict(Frame[] frames);
}

/// <summary>Планувальник черги відправлення (жадібний за щільністю). Власник: тімлід.</summary>
public interface IScheduler
{
    /// <summary>Порядок відправлення пакетів у межах бюджету часу.</summary>
    PacketPlan[] Order(PacketPlan[] packets, double budgetSeconds);
}

/// <summary>KDF + AES-256-CTR + HMAC-SHA256 (FORMAT_SPEC §4–5). Власник: Коля.</summary>
public interface ICrypto
{
    /// <summary>PBKDF2-HMAC-SHA256, 100000 ітерацій, 64 Б → (K_enc, K_mac). Один раз на сеанс.</summary>
    (byte[] KEnc, byte[] KMac) DeriveKeys(string password, byte[] salt);

    /// <summary>Шифрує навантаження і дописує HMAC: header ‖ ciphertext ‖ tag.</summary>
    byte[] Seal(byte[] header, byte[] payload);

    /// <summary>Перевіряє і розшифровує пакет строго в порядку FORMAT_SPEC §5.</summary>
    OpenResult Open(byte[] packet);
}

/// <summary>Генетичний алгоритм / випадковий пошук. Власник: Георгій.</summary>
public interface IOptimizer
{
    /// <summary>Максимізує fitness (Q за кількома seed каналу).</summary>
    (Params Best, EvolutionLog Log) Run(Func<Params, double> fitness, int seed);
}

/// <summary>Канал (незмінений channel.py через channel/bridge.py). Власник: тімлід.</summary>
public interface IChannel
{
    /// <summary>Передає пакет; arrival — час прибуття в секундах, якщо доставлено.</summary>
    (bool Delivered, double? Arrival) Send(byte[] packet);

    /// <summary>Статистика каналу (словник stats() з channel.py).</summary>
    ChannelStats Stats();
}

/// <summary>Готовий до відправлення пакет (до присвоєння seq і шифрування).</summary>
/// <param name="Index">Порядковий номер створення (для FIFO і розв'язання рівностей).</param>
/// <param name="Payload">Відкрите навантаження (FORMAT_SPEC §3).</param>
/// <param name="Frames">Кадри пакета, впорядковані за timestamp_ms.</param>
/// <param name="TotalValue">V_k — сума цінностей v_i кадрів.</param>
public sealed record PacketPlan(int Index, byte[] Payload, Frame[] Frames, int TotalValue)
{
    /// <summary>Фіксовані байти пакета поза навантаженням: заголовок 43 + HMAC 32.</summary>
    public const int Overhead = 75;

    /// <summary>b_k — розмір пакета в бітах: 8·(75 + довжина навантаження); CTR без доповнення.</summary>
    public long SizeBits => 8L * (Overhead + Payload.Length);
}

/// <summary>Коди відмови приймача (FORMAT_SPEC §6). Назви збігаються зі специфікацією.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1707", Justification = "Коди з FORMAT_SPEC §6")]
public enum PacketError
{
    /// <summary>Пакет прийнято.</summary>
    None = 0,
    /// <summary>Пакет коротший за 75 байтів.</summary>
    E_SHORT,
    /// <summary>Невірний magic або version.</summary>
    E_MAGIC,
    /// <summary>Довжина не відповідає ciphertext_len.</summary>
    E_LENGTH,
    /// <summary>HMAC не збігається (у т.ч. невірний пароль).</summary>
    E_HMAC,
    /// <summary>reserved ≠ 0, невідомі flags, неузгоджені секції, symbol_count ≠ 12·frame_count.</summary>
    E_PAYLOAD,
    /// <summary>Довжина коду &gt; 15, символ поза таблицею, обірваний потік.</summary>
    E_CODE,
}

/// <summary>Результат розбору пакета приймачем.</summary>
/// <param name="Error">Код відмови або <see cref="PacketError.None"/>.</param>
/// <param name="PacketSeq">packet_seq із заголовка (якщо заголовок розібрано).</param>
/// <param name="Payload">Розшифроване навантаження (лише якщо HMAC збігся).</param>
public sealed record OpenResult(PacketError Error, uint? PacketSeq, byte[]? Payload)
{
    /// <summary>Чи пакет прийнято.</summary>
    public bool Ok => Error == PacketError.None;

    /// <summary>Відмова з кодом без вмісту.</summary>
    public static OpenResult Fail(PacketError error) => new(error, null, null);
}

/// <summary>Хромосома ГА (базовий рівень: 3 гени).</summary>
/// <param name="HuffmanWindowFrames">Розмір вікна/блоку Гаффмана, кадрів.</param>
/// <param name="NoCompressionThreshold">Поріг вибору NO_COMPRESSION (пастка 19).</param>
/// <param name="PriorityWeight">Вага пріоритету в плануванні.</param>
public sealed record Params(int HuffmanWindowFrames, double NoCompressionThreshold, double PriorityWeight);

/// <summary>Рядок журналу еволюції ГА.</summary>
public sealed record GenerationRecord(int Generation, double Best, double Mean);

/// <summary>Журнал еволюції ГА (зберігається у файл і в репозиторій).</summary>
public sealed class EvolutionLog
{
    /// <summary>Записи за поколіннями.</summary>
    public List<GenerationRecord> Generations { get; } = new();
}

/// <summary>Статистика каналу; поля відповідають ключам stats() у channel.py.</summary>
public sealed record ChannelStats(
    int Sent,
    int Lost,
    int OverBudget,
    double LossRate,
    double UndeliveredRate,
    double ChannelTimeSeconds,
    double BudgetSeconds);
