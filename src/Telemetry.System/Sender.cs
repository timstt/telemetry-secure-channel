using System.Buffers.Binary;
using System.Security.Cryptography;
using Telemetry.Core;
using Telemetry.Huffman;
using Telemetry.Packet;

namespace Telemetry.System;

/// <summary>Режим стиснення пакетів.</summary>
public enum CompressionMode
{
    /// <summary>На кожен пакет: NO_COMPRESSION, якщо ентропія вікна ≥ порогу, інакше режим з більшою цінністю на біт.</summary>
    Auto,

    /// <summary>Завжди Гаффман за таблицею вікна (NO_COMPRESSION лише якщо рюкзак нічого не взяв).</summary>
    Huffman,

    /// <summary>Завжди NO_COMPRESSION (вимкнений блок «Стиснення» для таблиці 2).</summary>
    None,
}

/// <summary>Налаштування передавача.</summary>
/// <param name="Params">Гени ГА: вікно Гаффмана, поріг NO_COMPRESSION, вага пріоритету в черзі.</param>
/// <param name="Compression">Режим стиснення.</param>
/// <param name="UseKnapsack">false — пакет заповнюється кадрами черги по порядку, без рюкзака (таблиця 2).</param>
/// <param name="DuplicateCritical">
/// Командне розширення DUPLICATION (0x02): кожен критичний кадр повторно передається в окремому пакеті
/// наприкінці черги (рознесено від оригіналу, бо втрати пачками). Приймач з цим розширенням зараховує дубль один раз.
/// </param>
/// <param name="DeviceId">device_id у заголовку.</param>
/// <param name="Salt">Сіль сеансу (16 Б); null — випадкова.</param>
/// <param name="NoncePrefix">Випадкова частина nonce сеансу (8 Б); null — випадкова.</param>
public sealed record SenderOptions(
    Params? Params = null,
    CompressionMode Compression = CompressionMode.Auto,
    bool UseKnapsack = true,
    bool DuplicateCritical = true,
    ushort DeviceId = 1,
    byte[]? Salt = null,
    byte[]? NoncePrefix = null)
{
    /// <summary>Параметри за замовчуванням: вікно 32, поріг ентропії 7,5 біт/байт, вага пріоритету 1.</summary>
    public static Params DefaultParams { get; } = new(32, 7.5, 1.0);

    /// <summary>Фактичні параметри.</summary>
    public Params EffectiveParams => Params ?? DefaultParams;
}

/// <summary>
/// Передавач: класифікація → черга за цінністю (пастка 18) → вікно → таблиця Гаффмана → ДП-вибір →
/// вибір режиму Гаффман/NO_COMPRESSION (пастка 19) → пакет → планувальник → seq після планувальника →
/// шифрування → канал. Власник: тімлід.
/// </summary>
/// <remarks>
/// <para>
/// Черга: кадри за спаданням ключа <c>PriorityWeight·пріоритет(клас) + critical_бонус·critical</c>, рівні —
/// за timestamp_ms. Вікно кандидатів — перші <c>HuffmanWindowFrames</c> кадрів черги (не більше
/// candidate_window_frames = 64). Для вікна будується ОДНА таблиця Гаффмана; w_i — довжина 12 байтів кадру за нею;
/// W = 8·(max_payload − 14 − 2·table_nsym); рюкзак розв'язується з цінностями v_i зі специфікації. Обрані кадри
/// кодуються тією самою таблицею, і в пакет пишуться довжини всіх символів вікна, тому розмір пакета точно
/// дорівнює розрахованому (≤ max_packet_bytes). Невибрані кадри лишаються в черзі на своїх місцях.
/// </para>
/// <para>
/// Альтернатива — NO_COMPRESSION з першими ⌊(max_payload − 14)/12⌋ кадрами черги. В Auto він обирається, якщо
/// ентропія байтів вікна не нижча за поріг (C2 п.5), інакше — варіант з більшою цінністю на біт пакета (пастка 19).
/// Якщо рюкзак нічого не взяв (таблиця не лишила місця), теж NO_COMPRESSION — так черга завжди скорочується.
/// </para>
/// <para>CLASS_TABLE у пакети не пишеться (FORMAT_SPEC §3.5).</para>
/// </remarks>
public sealed class Sender
{
    private readonly IClassifier classifier;
    private readonly IPacker packer;
    private readonly TeamConfig config;
    private readonly ICrypto crypto;
    private readonly SenderOptions options;
    private readonly HuffmanCodec codec = new();
    private readonly byte[] salt;
    private readonly byte[] noncePrefix;

    /// <summary>Створює передавач сеансу.</summary>
    /// <param name="config">team_config.json (ліміт пакета, вікно, пріоритети).</param>
    /// <param name="crypto">Шифрування сеансу (пароль усередині).</param>
    /// <param name="classifier">Класифікатор кадрів.</param>
    /// <param name="packer">Рюкзак (ДП або жадібний).</param>
    /// <param name="options">Налаштування.</param>
    /// <exception cref="ArgumentException">Сіль не 16 Б, префікс nonce не 8 Б, або в пакет не влазить жоден кадр.</exception>
    public Sender(TeamConfig config, ICrypto crypto, IClassifier classifier, IPacker packer, SenderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(crypto);
        ArgumentNullException.ThrowIfNull(classifier);
        ArgumentNullException.ThrowIfNull(packer);
        this.config = config;
        this.crypto = crypto;
        this.classifier = classifier;
        this.packer = packer;
        this.options = options ?? new SenderOptions();
        salt = this.options.Salt ?? RandomNumberGenerator.GetBytes(16);
        noncePrefix = this.options.NoncePrefix ?? RandomNumberGenerator.GetBytes(8);
        if (salt.Length != 16 || noncePrefix.Length != 8)
        {
            throw new ArgumentException("Сіль має бути 16 байтів, префікс nonce — 8.", nameof(options));
        }

        if (MaxRawFrames < 1)
        {
            throw new ArgumentException("max_packet_bytes замалий: у пакет не вміщується жоден кадр.", nameof(config));
        }
    }

    /// <summary>Скільки кадрів уміщує пакет NO_COMPRESSION.</summary>
    public int MaxRawFrames => (config.Packing.MaxPayloadBytes - PacketCodec.FixedPayloadBytes) / Frame.Size;

    /// <summary>Скільки пакетів останнього <see cref="BuildPackets"/> закодовано Гаффманом.</summary>
    public int HuffmanPackets { get; private set; }

    /// <summary>Скільки пакетів останнього <see cref="BuildPackets"/> — NO_COMPRESSION.</summary>
    public int RawPackets { get; private set; }

    /// <summary>Формує пакети сеансу (до присвоєння seq і шифрування).</summary>
    public PacketPlan[] BuildPackets(Frame[] frames)
    {
        ArgumentNullException.ThrowIfNull(frames);
        HuffmanPackets = 0;
        RawPackets = 0;
        if (frames.Length == 0)
        {
            return [];
        }

        var p = options.EffectiveParams;
        var classes = classifier.Predict(frames);
        if (classes.Length != frames.Length)
        {
            throw new InvalidOperationException("Класифікатор повернув не стільки класів, скільки кадрів.");
        }

        var pr = config.Priorities;
        var items = frames.Select((f, i) => new Item(
            f,
            pr.Value(classes[i], f.IsCritical),
            (p.PriorityWeight * pr.ForClass(classes[i])) + (f.IsCritical ? pr.CriticalBonus : 0))).ToList();

        var queue = items
            .OrderByDescending(x => x.Key)
            .ThenBy(x => x.Frame.TimestampMs)
            .ToList();

        var plans = new List<PacketPlan>();
        var duplicates = new List<Item>();
        while (queue.Count > 0)
        {
            var (chosen, packetFlags, lengths) = NextPacket(queue, p);
            plans.Add(MakePlan(plans.Count, chosen, packetFlags, lengths));
            if (packetFlags.HasFlag(PayloadFlags.NoCompression))
            {
                RawPackets++;
            }
            else
            {
                HuffmanPackets++;
            }
            if (options.DuplicateCritical)
            {
                duplicates.AddRange(chosen.Where(x => x.Frame.IsCritical));
            }

            var taken = chosen.ToHashSet();
            queue.RemoveAll(taken.Contains);
        }

        // Дублі критичних — окремі пакети NO_COMPRESSION наприкінці черги, рознесені від оригіналів.
        for (var start = 0; start < duplicates.Count; start += MaxRawFrames)
        {
            var chunk = duplicates.Skip(start).Take(MaxRawFrames).ToList();
            plans.Add(MakePlan(plans.Count, chunk, PayloadFlags.NoCompression | PayloadFlags.Duplication, new byte[256]));
            RawPackets++;
        }

        return [.. plans];
    }

    /// <summary>Присвоює seq у порядку відправлення, шифрує і віддає в канал.</summary>
    public void Transmit(PacketPlan[] ordered, IChannel channel)
    {
        ArgumentNullException.ThrowIfNull(ordered);
        ArgumentNullException.ThrowIfNull(channel);
        foreach (var packet in Seal(ordered))
        {
            channel.Send(packet);
        }
    }

    /// <summary>Шифровані пакети в заданому порядку; seq = 0, 1, … (nonce = префікс сеансу ‖ uint32_BE(seq)).</summary>
    public IEnumerable<byte[]> Seal(IEnumerable<PacketPlan> ordered)
    {
        ArgumentNullException.ThrowIfNull(ordered);
        uint seq = 0;
        foreach (var plan in ordered)
        {
            var nonce = new byte[12];
            noncePrefix.CopyTo(nonce, 0);
            BinaryPrimitives.WriteUInt32BigEndian(nonce.AsSpan(8), seq);
            var header = PacketCodec.WriteHeader(new PacketHeader(options.DeviceId, seq, salt, nonce, (uint)plan.Payload.Length));
            yield return crypto.Seal(header, plan.Payload);
            seq++;
        }
    }

    private (List<Item> Chosen, PayloadFlags Flags, byte[] Lengths) NextPacket(List<Item> queue, Params p)
    {
        var windowSize = Math.Clamp(p.HuffmanWindowFrames, 1, config.Packing.CandidateWindowFrames);
        var window = queue.Take(windowSize).ToList();
        var raw = queue.Take(MaxRawFrames).ToList();
        if (options.Compression == CompressionMode.None)
        {
            return (raw, PayloadFlags.NoCompression, new byte[256]);
        }

        var windowBytes = Frame.Serialize([.. window.Select(x => x.Frame)]);
        var freq = new long[256];
        foreach (var b in windowBytes)
        {
            freq[b]++;
        }

        var lengths = codec.BuildLengths(freq);
        var huffman = SelectHuffman(window, lengths);
        if (huffman.Count == 0)
        {
            return (raw, PayloadFlags.NoCompression, new byte[256]);
        }

        if (options.Compression == CompressionMode.Huffman)
        {
            return (huffman, PayloadFlags.None, lengths);
        }

        if (NoCompression.ShouldSkipCompression(windowBytes, p.NoCompressionThreshold))
        {
            return (raw, PayloadFlags.NoCompression, new byte[256]);
        }

        // Пастка 19: режим з більшою цінністю на біт пакета (V / b), рівність — на користь Гаффмана.
        var huffmanBits = 8L * (PacketPlan.Overhead + PacketCodec.PayloadSize(PayloadFlags.None, lengths, [.. huffman.Select(x => x.Frame)]));
        var rawBits = 8L * (PacketPlan.Overhead + PacketCodec.FixedPayloadBytes + (Frame.Size * raw.Count));
        var huffmanValue = huffman.Sum(x => (long)x.Value);
        var rawValue = raw.Sum(x => (long)x.Value);
        return huffmanValue * rawBits >= rawValue * huffmanBits
            ? (huffman, PayloadFlags.None, lengths)
            : (raw, PayloadFlags.NoCompression, new byte[256]);
    }

    private List<Item> SelectHuffman(List<Item> window, byte[] lengths)
    {
        var capacity = config.Packing.CapacityBits(PacketCodec.TableSymbols(lengths));
        var weights = window.Select(x => FrameBits(x.Frame, lengths)).ToArray();
        if (!options.UseKnapsack)
        {
            var taken = new List<Item>();
            var used = 0;
            for (var i = 0; i < window.Count && used + weights[i] <= capacity; i++)
            {
                taken.Add(window[i]);
                used += weights[i];
            }

            return taken;
        }

        var values = window.Select(x => x.Value).ToArray();
        return [.. packer.Select(weights, values, capacity).Select(i => window[i])];
    }

    private static int FrameBits(Frame f, byte[] lengths)
    {
        Span<byte> b = stackalloc byte[Frame.Size];
        f.WriteTo(b);
        var bits = 0;
        foreach (var x in b)
        {
            bits += lengths[x];
        }

        return bits;
    }

    private PacketPlan MakePlan(int index, List<Item> chosen, PayloadFlags flags, byte[] lengths)
    {
        var frames = chosen.Select(x => x.Frame).OrderBy(f => f.TimestampMs).ToArray();
        var payload = PacketCodec.WritePayload(new Payload(flags, lengths, frames, null), codec);
        if (PacketPlan.Overhead + payload.Length > config.Packing.MaxPacketBytes)
        {
            throw new InvalidOperationException("Пакет перевищив max_packet_bytes — порушено інваріант рюкзака.");
        }

        return new PacketPlan(index, payload, frames, chosen.Sum(x => x.Value));
    }

    /// <summary>Кадр у черзі; рівність за посиланням, тож однакові кадри сеансу не зливаються.</summary>
    private sealed class Item(Frame frame, int value, double key)
    {
        public Frame Frame { get; } = frame;

        public int Value { get; } = value;

        public double Key { get; } = key;
    }
}
