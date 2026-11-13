using System.Diagnostics.CodeAnalysis;
using Telemetry.Core;

namespace Telemetry.Packet;

/// <summary>Заголовок пакета, 43 байти відкритим текстом (FORMAT_SPEC §2.1).</summary>
public sealed record PacketHeader(ushort DeviceId, uint PacketSeq, byte[] Salt, byte[] Nonce, uint CiphertextLength)
{
    /// <summary>Розмір заголовка.</summary>
    public const int Size = 43;

    /// <summary>Мінімальна довжина пакета: 43 + 0 + 32.</summary>
    public const int MinPacketLength = 75;
}

/// <summary>Прапорці навантаження (FORMAT_SPEC §3.1).</summary>
[Flags]
[SuppressMessage("Naming", "CA1711", Justification = "Назва з FORMAT_SPEC")]
public enum PayloadFlags : byte
{
    /// <summary>Базовий формат.</summary>
    None = 0,
    /// <summary>Командне розширення: залишки прогнозу.</summary>
    Prediction = 0x01,
    /// <summary>Командне розширення: дублювання критичних кадрів.</summary>
    Duplication = 0x02,
    /// <summary>Присутня секція class_table.</summary>
    ClassTable = 0x04,
    /// <summary>Сирі байти без Гаффмана.</summary>
    NoCompression = 0x08,
}

/// <summary>Розібране навантаження (FORMAT_SPEC §3).</summary>
public sealed record Payload(PayloadFlags Flags, byte[] Lengths, Frame[] Frames, byte[]? ClassTable);

/// <summary>Серіалізація заголовка і навантаження з перевіркою інваріантів. Власник: Коля.</summary>
[ExcludeFromCodeCoverage(Justification = "Заглушка каркаса; прибрати атрибут разом з реалізацією")]
public static class PacketCodec
{
    /// <summary>Записує заголовок big-endian.</summary>
    public static byte[] WriteHeader(PacketHeader header) => throw new NotImplementedException();

    /// <summary>Будує навантаження: frame_count, flags, reserved=0, таблиця, symbol_count, потік[, class_table].</summary>
    public static byte[] WritePayload(Payload payload, IHuffmanCodec codec) => throw new NotImplementedException();

    /// <summary>Розбирає навантаження; symbol_count ≠ 12·frame_count → E_PAYLOAD.</summary>
    public static Payload ReadPayload(byte[] plaintext, IHuffmanCodec codec) => throw new NotImplementedException();
}
