using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;

namespace Telemetry.Core;

/// <summary>
/// Кадр телеметрії: рівно 12 байтів big-endian (FORMAT_SPEC §1).
/// Власник: Ваня. Серіалізація, розбір і валідація входу (C2 п.1) — його зона.
/// </summary>
/// <param name="TimestampMs">Час від початку сеансу, мс (uint32).</param>
/// <param name="AltitudeDm">Висота, дециметри (int16).</param>
/// <param name="TempC10">Температура, десяті °C (int16).</param>
/// <param name="Vibration">Віброприскорення, умовні одиниці (int16).</param>
/// <param name="Mode">Режим апарата 0…7 (uint8).</param>
/// <param name="Critical">0 — звичайний, 1 — критичний (uint8).</param>
public readonly record struct Frame(
    uint TimestampMs,
    short AltitudeDm,
    short TempC10,
    short Vibration,
    byte Mode,
    byte Critical)
{
    /// <summary>Розмір серіалізованого кадру в байтах.</summary>
    public const int Size = 12;

    /// <summary>Найбільше допустиме значення mode.</summary>
    public const byte MaxMode = 7;

    /// <summary>Чи кадр критичний (critical == 1).</summary>
    public bool IsCritical => Critical == 1;

    /// <summary>Чи поля mode і critical у допустимих межах (mode 0…7, critical 0/1).</summary>
    public bool IsValid => Mode <= MaxMode && Critical <= 1;

    /// <summary>Записує кадр у 12 байтів big-endian (BinaryPrimitives).</summary>
    /// <exception cref="ArgumentException">Буфер коротший за 12 байтів.</exception>
    public void WriteTo(Span<byte> destination)
    {
        if (destination.Length < Size)
        {
            throw new ArgumentException("Буфер кадру має бути щонайменше 12 байтів.", nameof(destination));
        }

        BinaryPrimitives.WriteUInt32BigEndian(destination, TimestampMs);
        BinaryPrimitives.WriteInt16BigEndian(destination[4..], AltitudeDm);
        BinaryPrimitives.WriteInt16BigEndian(destination[6..], TempC10);
        BinaryPrimitives.WriteInt16BigEndian(destination[8..], Vibration);
        destination[10] = Mode;
        destination[11] = Critical;
    }

    /// <summary>12 байтів кадру новим масивом.</summary>
    public byte[] ToBytes()
    {
        var bytes = new byte[Size];
        WriteTo(bytes);
        return bytes;
    }

    /// <summary>Читає кадр із 12 байтів big-endian і перевіряє діапазони (mode 0…7, critical 0/1).</summary>
    /// <exception cref="ArgumentException">Менше 12 байтів.</exception>
    /// <exception cref="FormatException">mode або critical поза допустимими значеннями.</exception>
    public static Frame Read(ReadOnlySpan<byte> source)
    {
        if (source.Length < Size)
        {
            throw new ArgumentException("Кадр має бути щонайменше 12 байтів.", nameof(source));
        }

        var frame = new Frame(
            BinaryPrimitives.ReadUInt32BigEndian(source),
            BinaryPrimitives.ReadInt16BigEndian(source[4..]),
            BinaryPrimitives.ReadInt16BigEndian(source[6..]),
            BinaryPrimitives.ReadInt16BigEndian(source[8..]),
            source[10],
            source[11]);
        frame.Validate();
        return frame;
    }

    /// <summary>Кадри з послідовності байтів, кратної 12.</summary>
    /// <exception cref="FormatException">Довжина не кратна 12 або кадр невалідний.</exception>
    public static Frame[] ReadAll(ReadOnlySpan<byte> source)
    {
        if (source.Length % Size != 0)
        {
            throw new FormatException("Довжина даних кадрів не кратна 12.");
        }

        var frames = new Frame[source.Length / Size];
        for (var i = 0; i < frames.Length; i++)
        {
            frames[i] = Read(source.Slice(i * Size, Size));
        }

        return frames;
    }

    /// <summary>Байти всіх кадрів підряд (12 на кадр).</summary>
    public static byte[] Serialize(IReadOnlyList<Frame> frames)
    {
        ArgumentNullException.ThrowIfNull(frames);
        var bytes = new byte[frames.Count * Size];
        for (var i = 0; i < frames.Count; i++)
        {
            frames[i].WriteTo(bytes.AsSpan(i * Size, Size));
        }

        return bytes;
    }

    /// <summary>
    /// Розбирає рядок CSV (timestamp_ms,altitude_dm,temp_c10,vibration,mode,critical[,label])
    /// з валідацією діапазонів, типів і пропусків (C2 п.1). Сьома колонка (мітка класу) ігнорується.
    /// </summary>
    /// <exception cref="FormatException">Пропуск, нечисло, вихід за межі типу, mode ∉ 0…7, critical ∉ {0,1}.</exception>
    public static Frame ParseCsv(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        var parts = line.Split(',');
        if (parts.Length is < 6 or > 7)
        {
            throw new FormatException($"очікується 6 або 7 полів, отримано {parts.Length}");
        }

        var frame = new Frame(
            ParseField<uint>(parts[0], "timestamp_ms"),
            ParseField<short>(parts[1], "altitude_dm"),
            ParseField<short>(parts[2], "temp_c10"),
            ParseField<short>(parts[3], "vibration"),
            ParseField<byte>(parts[4], "mode"),
            ParseField<byte>(parts[5], "critical"));
        frame.Validate();
        return frame;
    }

    /// <summary>Мітка класу із сьомої колонки CSV (train/valid), якщо вона є.</summary>
    /// <exception cref="FormatException">Мітка відсутня або не 0…2.</exception>
    public static int ParseLabel(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        var parts = line.Split(',');
        if (parts.Length != 7)
        {
            throw new FormatException("рядок не містить мітки класу");
        }

        var label = ParseField<byte>(parts[6], "label");
        return label <= 2 ? label : throw new FormatException($"мітка {label} поза 0…2");
    }

    private void Validate()
    {
        if (Mode > MaxMode)
        {
            throw new FormatException($"mode = {Mode} поза 0…7");
        }

        if (Critical > 1)
        {
            throw new FormatException($"critical = {Critical} не 0 і не 1");
        }
    }

    private static T ParseField<T>(string text, string name)
        where T : IBinaryInteger<T>
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0)
        {
            throw new FormatException($"поле {name} порожнє");
        }

        if (!long.TryParse(trimmed, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var wide))
        {
            throw new FormatException($"поле {name} не є цілим числом");
        }

        if (!TryChecked(wide, out T value))
        {
            throw new FormatException($"поле {name} = {wide} поза межами типу");
        }

        return value;
    }

    private static bool TryChecked<T>(long wide, out T value)
        where T : IBinaryInteger<T>
    {
        try
        {
            value = T.CreateChecked(wide);
            return true;
        }
        catch (OverflowException)
        {
            value = T.Zero;
            return false;
        }
    }
}
