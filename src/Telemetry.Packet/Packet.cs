using System.Buffers.Binary;
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

    /// <summary>ASCII «TLM1».</summary>
    public static ReadOnlySpan<byte> Magic => "TLM1"u8;

    /// <summary>Версія формату.</summary>
    public const byte Version = 1;
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
/// <param name="Flags">Прапорці.</param>
/// <param name="Lengths">Довжини кодів (256, індекс = символ); для NO_COMPRESSION ігноруються.</param>
/// <param name="Frames">Кадри в порядку запису.</param>
/// <param name="ClassTable">Класи кадрів, лише з прапорцем CLASS_TABLE.</param>
public sealed record Payload(PayloadFlags Flags, byte[] Lengths, Frame[] Frames, byte[]? ClassTable);

/// <summary>Серіалізація заголовка і навантаження з перевіркою інваріантів. Власник: Коля.</summary>
public static class PacketCodec
{
    /// <summary>Фіксовані поля навантаження: frame_count, flags, reserved, table_nsym, symbol_count, bitstream_len.</summary>
    public const int FixedPayloadBytes = 14;

    /// <summary>Найбільша допустима довжина коду.</summary>
    public const int MaxCodeLength = 15;

    private const int AlphabetSize = 256;

    /// <summary>Записує заголовок big-endian.</summary>
    /// <exception cref="ArgumentException">Сіль не 16 байтів або nonce не 12.</exception>
    public static byte[] WriteHeader(PacketHeader header)
    {
        ArgumentNullException.ThrowIfNull(header);
        if (header.Salt is not { Length: 16 })
        {
            throw new ArgumentException("Сіль має бути 16 байтів.", nameof(header));
        }

        if (header.Nonce is not { Length: 12 })
        {
            throw new ArgumentException("Nonce має бути 12 байтів.", nameof(header));
        }

        var bytes = new byte[PacketHeader.Size];
        PacketHeader.Magic.CopyTo(bytes);
        bytes[4] = PacketHeader.Version;
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(5), header.DeviceId);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(7), header.PacketSeq);
        header.Salt.CopyTo(bytes, 11);
        header.Nonce.CopyTo(bytes, 27);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(39), header.CiphertextLength);
        return bytes;
    }

    /// <summary>Читає заголовок (без перевірки HMAC — лише структура).</summary>
    /// <exception cref="PacketFormatException">E_SHORT або E_MAGIC.</exception>
    public static PacketHeader ReadHeader(ReadOnlySpan<byte> packet)
    {
        if (packet.Length < PacketHeader.Size)
        {
            throw new PacketFormatException(PacketError.E_SHORT);
        }

        if (!packet[..4].SequenceEqual(PacketHeader.Magic) || packet[4] != PacketHeader.Version)
        {
            throw new PacketFormatException(PacketError.E_MAGIC);
        }

        return new PacketHeader(
            BinaryPrimitives.ReadUInt16BigEndian(packet[5..]),
            BinaryPrimitives.ReadUInt32BigEndian(packet[7..]),
            packet[11..27].ToArray(),
            packet[27..39].ToArray(),
            BinaryPrimitives.ReadUInt32BigEndian(packet[39..]));
    }

    /// <summary>Кількість символів із ненульовою довжиною коду.</summary>
    public static int TableSymbols(byte[] lengths)
    {
        ArgumentNullException.ThrowIfNull(lengths);
        return lengths.Count(l => l != 0);
    }

    /// <summary>
    /// Точна довжина навантаження без побудови потоку: 14 + 2·nsym + ⌈Σ len / 8⌉ (+ frame_count з CLASS_TABLE);
    /// для NO_COMPRESSION — 14 + 12·frame_count.
    /// </summary>
    public static int PayloadSize(PayloadFlags flags, byte[] lengths, IReadOnlyList<Frame> frames)
    {
        ArgumentNullException.ThrowIfNull(lengths);
        ArgumentNullException.ThrowIfNull(frames);
        var classTable = flags.HasFlag(PayloadFlags.ClassTable) ? frames.Count : 0;
        if (flags.HasFlag(PayloadFlags.NoCompression))
        {
            return FixedPayloadBytes + (Frame.Size * frames.Count) + classTable;
        }

        long bits = 0;
        Span<byte> buffer = stackalloc byte[Frame.Size];
        foreach (var f in frames)
        {
            f.WriteTo(buffer);
            foreach (var b in buffer)
            {
                bits += lengths[b];
            }
        }

        return checked(FixedPayloadBytes + (2 * TableSymbols(lengths)) + (int)((bits + 7) / 8) + classTable);
    }

    /// <summary>Будує навантаження: frame_count, flags, reserved=0, таблиця, symbol_count, потік[, class_table].</summary>
    /// <remarks>
    /// У таблицю пишуться ВСІ символи з ненульовою довжиною в <see cref="Payload.Lengths"/>, навіть ті, яких
    /// немає в кадрах пакета: так розмір збігається з місткістю рюкзака, порахованою для таблиці вікна.
    /// </remarks>
    /// <exception cref="ArgumentException">Забагато кадрів, некоректна class_table чи довжини.</exception>
    public static byte[] WritePayload(Payload payload, IHuffmanCodec codec)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(codec);
        ArgumentNullException.ThrowIfNull(payload.Frames);
        if (payload.Frames.Length > ushort.MaxValue)
        {
            throw new ArgumentException("Забагато кадрів для одного пакета.", nameof(payload));
        }

        var withClassTable = payload.Flags.HasFlag(PayloadFlags.ClassTable);
        if (withClassTable != payload.ClassTable is not null
            || (payload.ClassTable is not null && payload.ClassTable.Length != payload.Frames.Length))
        {
            throw new ArgumentException("class_table має бути присутня рівно з прапорцем CLASS_TABLE і мати frame_count байтів.", nameof(payload));
        }

        var raw = Frame.Serialize(payload.Frames);
        var noCompression = payload.Flags.HasFlag(PayloadFlags.NoCompression);
        byte[] bitstream;
        var table = new List<(byte Symbol, byte Length)>();
        if (noCompression)
        {
            bitstream = raw;
        }
        else
        {
            ArgumentNullException.ThrowIfNull(payload.Lengths);
            if (payload.Lengths.Length != AlphabetSize)
            {
                throw new ArgumentException("Очікується 256 довжин кодів.", nameof(payload));
            }

            for (var s = 0; s < AlphabetSize; s++)
            {
                if (payload.Lengths[s] > MaxCodeLength)
                {
                    throw new ArgumentException("Довжина коду більша за 15.", nameof(payload));
                }

                if (payload.Lengths[s] != 0)
                {
                    table.Add(((byte)s, payload.Lengths[s]));
                }
            }

            bitstream = raw.Length == 0 ? [] : codec.Encode(raw, payload.Lengths).Data;
        }

        var size = FixedPayloadBytes + (2 * table.Count) + bitstream.Length + (withClassTable ? payload.Frames.Length : 0);
        var bytes = new byte[size];
        var span = bytes.AsSpan();
        BinaryPrimitives.WriteUInt16BigEndian(span, (ushort)payload.Frames.Length);
        span[2] = (byte)payload.Flags;
        span[3] = 0;
        BinaryPrimitives.WriteUInt16BigEndian(span[4..], (ushort)table.Count);
        var pos = 6;
        foreach (var (symbol, length) in table)
        {
            span[pos++] = symbol;
            span[pos++] = length;
        }

        BinaryPrimitives.WriteUInt32BigEndian(span[pos..], (uint)raw.Length);
        BinaryPrimitives.WriteUInt32BigEndian(span[(pos + 4)..], (uint)bitstream.Length);
        pos += 8;
        bitstream.CopyTo(span[pos..]);
        pos += bitstream.Length;
        payload.ClassTable?.CopyTo(span[pos..]);
        return bytes;
    }

    /// <summary>Розбирає навантаження; symbol_count ≠ 12·frame_count → E_PAYLOAD.</summary>
    /// <param name="plaintext">Розшифроване навантаження.</param>
    /// <param name="codec">Декодер Гаффмана.</param>
    /// <param name="allowed">Командні розширення, які приймач підтримує (PREDICTION/DUPLICATION); решта → E_PAYLOAD.</param>
    /// <exception cref="PacketFormatException">E_PAYLOAD або E_CODE (FORMAT_SPEC §6).</exception>
    public static Payload ReadPayload(byte[] plaintext, IHuffmanCodec codec, PayloadFlags allowed = PayloadFlags.None)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        ArgumentNullException.ThrowIfNull(codec);
        var span = plaintext.AsSpan();
        Require(span.Length >= 6, PacketError.E_PAYLOAD);

        int frameCount = BinaryPrimitives.ReadUInt16BigEndian(span);
        var flags = (PayloadFlags)span[2];
        Require(span[3] == 0, PacketError.E_PAYLOAD);                               // reserved
        Require(((byte)flags & 0xF0) == 0, PacketError.E_PAYLOAD);                  // невикористані біти
        var extensions = flags & (PayloadFlags.Prediction | PayloadFlags.Duplication);
        Require((extensions & ~allowed) == 0, PacketError.E_PAYLOAD);               // невідоме розширення
        Require(!flags.HasFlag(PayloadFlags.Prediction), PacketError.E_PAYLOAD);    // залишки прогнозу не реалізовано

        int nsym = BinaryPrimitives.ReadUInt16BigEndian(span[4..]);
        var noCompression = flags.HasFlag(PayloadFlags.NoCompression);
        Require(!noCompression || nsym == 0, PacketError.E_PAYLOAD);
        Require(nsym <= AlphabetSize, PacketError.E_PAYLOAD);
        var pos = 6;
        Require(span.Length - pos >= (2 * nsym) + 8, PacketError.E_PAYLOAD);

        var lengths = new byte[AlphabetSize];
        var previous = -1;
        long kraft = 0;
        for (var i = 0; i < nsym; i++)
        {
            int symbol = span[pos++];
            var length = span[pos++];
            Require(symbol > previous, PacketError.E_PAYLOAD);                      // строго за зростанням символу
            Require(length is >= 1 and <= MaxCodeLength, PacketError.E_CODE);
            lengths[symbol] = length;
            kraft += 1L << (MaxCodeLength - length);
            previous = symbol;
        }

        Require(kraft <= 1L << MaxCodeLength, PacketError.E_CODE);

        long symbolCount = BinaryPrimitives.ReadUInt32BigEndian(span[pos..]);
        long bitstreamLength = BinaryPrimitives.ReadUInt32BigEndian(span[(pos + 4)..]);
        pos += 8;
        Require(symbolCount == (long)Frame.Size * frameCount, PacketError.E_PAYLOAD); // інваріант §3
        var classTableLength = flags.HasFlag(PayloadFlags.ClassTable) ? frameCount : 0;
        Require(span.Length - pos == bitstreamLength + classTableLength, PacketError.E_PAYLOAD);

        var bitstream = plaintext[pos..(pos + (int)bitstreamLength)];
        byte[] raw;
        if (noCompression)
        {
            Require(bitstreamLength == symbolCount, PacketError.E_PAYLOAD);
            raw = bitstream;
        }
        else
        {
            raw = Decode(codec, bitstream, lengths, (int)symbolCount);
        }

        var classTable = flags.HasFlag(PayloadFlags.ClassTable)
            ? plaintext[(pos + (int)bitstreamLength)..]
            : null;

        Frame[] frames;
        try
        {
            frames = Frame.ReadAll(raw);
        }
        catch (FormatException ex)
        {
            throw new PacketFormatException(PacketError.E_PAYLOAD, ex); // mode/critical поза межами
        }

        return new Payload(flags, lengths, frames, classTable);
    }

    private static byte[] Decode(IHuffmanCodec codec, byte[] bitstream, byte[] lengths, int symbolCount)
    {
        byte[] raw;
        try
        {
            raw = codec.Decode(bitstream, lengths, symbolCount);
        }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentException or IndexOutOfRangeException)
        {
            throw new PacketFormatException(PacketError.E_CODE, ex);
        }

        // Довжина потоку має точно відповідати декодованим бітам, а хвіст останнього байта — нулі (§0 п.4, §3.3).
        long bits = 0;
        foreach (var b in raw)
        {
            bits += lengths[b];
        }

        Require(bitstream.LongLength == (bits + 7) / 8, PacketError.E_PAYLOAD);
        var tail = (int)(bits % 8);
        if (tail != 0)
        {
            Require((bitstream[^1] & (0xFF >> tail)) == 0, PacketError.E_CODE);
        }

        return raw;
    }

    private static void Require(bool condition, PacketError code)
    {
        if (!condition)
        {
            throw new PacketFormatException(code);
        }
    }
}
