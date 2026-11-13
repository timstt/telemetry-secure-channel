using System.Diagnostics.CodeAnalysis;

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

    /// <summary>Чи кадр критичний (critical == 1).</summary>
    public bool IsCritical => Critical == 1;

    /// <summary>Записує кадр у 12 байтів big-endian (BinaryPrimitives).</summary>
    [ExcludeFromCodeCoverage(Justification = "Заглушка каркаса; прибрати атрибут разом з реалізацією")]
    public void WriteTo(Span<byte> destination) => throw new NotImplementedException();

    /// <summary>Читає кадр із 12 байтів big-endian і перевіряє діапазони (mode 0…7, critical 0/1).</summary>
    [ExcludeFromCodeCoverage(Justification = "Заглушка каркаса; прибрати атрибут разом з реалізацією")]
    public static Frame Read(ReadOnlySpan<byte> source) => throw new NotImplementedException();

    /// <summary>
    /// Розбирає рядок CSV (timestamp_ms,altitude_dm,temp_c10,vibration,mode,critical[,label])
    /// з валідацією діапазонів, типів і пропусків (C2 п.1).
    /// </summary>
    [ExcludeFromCodeCoverage(Justification = "Заглушка каркаса; прибрати атрибут разом з реалізацією")]
    public static Frame ParseCsv(string line) => throw new NotImplementedException();
}
