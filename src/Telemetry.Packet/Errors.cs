using Telemetry.Core;

namespace Telemetry.Packet;

/// <summary>
/// Виняток розбору з кодом відмови FORMAT_SPEC §6. Повідомлення НЕ містить вмісту пакета,
/// ключів чи пароля (C5 п.3). Власник: Коля.
/// </summary>
public sealed class PacketFormatException : Exception
{
    /// <summary>Створює виняток із кодом відмови.</summary>
    public PacketFormatException(PacketError code)
        : base(code.ToString()) => Code = code;

    /// <summary>Виняток із кодом відмови і причиною (причина не містить вмісту пакета).</summary>
    public PacketFormatException(PacketError code, Exception innerException)
        : base(code.ToString(), innerException) => Code = code;

    /// <summary>Порожній конструктор (вимога аналізатора).</summary>
    public PacketFormatException()
        : this(PacketError.E_PAYLOAD)
    {
    }

    /// <summary>Конструктор із повідомленням (вимога аналізатора).</summary>
    public PacketFormatException(string message)
        : base(message) => Code = PacketError.E_PAYLOAD;

    /// <summary>Конструктор із внутрішнім винятком (вимога аналізатора).</summary>
    public PacketFormatException(string message, Exception innerException)
        : base(message, innerException) => Code = PacketError.E_PAYLOAD;

    /// <summary>Код відмови.</summary>
    public PacketError Code { get; }
}
