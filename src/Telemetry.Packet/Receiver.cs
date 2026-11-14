using Telemetry.Core;
using Telemetry.Security;

namespace Telemetry.Packet;

/// <summary>
/// Приймач. Порядок (FORMAT_SPEC §5): довжина ≥ 75 → magic/version → довжина vs ciphertext_len →
/// HMAC у постійний час → лише потім розшифрування → reserved/flags → декодування.
/// Розмір &gt; max_packet_bytes НЕ є причиною відмови. Збирає кадри з усіх пакетів, ігнорує дублікати
/// seq, сортує кадри за timestamp_ms. Власник: Коля.
/// </summary>
public sealed class Receiver
{
    private readonly ICrypto crypto;
    private readonly IHuffmanCodec codec;
    private readonly PayloadFlags allowedExtensions;
    private readonly HashSet<uint> seenSeq = [];
    private readonly List<Frame> frames = [];
    private readonly HashSet<Frame>? uniqueFrames;
    private readonly Dictionary<PacketError, int> rejected = [];
    private List<Frame>? sorted;

    /// <summary>Створює приймач для пароля сеансу.</summary>
    public Receiver(string password, IHuffmanCodec codec)
        : this(new SessionCrypto(password), codec)
    {
    }

    /// <summary>Приймач поверх готової криптографії сеансу.</summary>
    /// <param name="crypto">Перевірка й розшифрування (кроки 1–6).</param>
    /// <param name="codec">Декодер Гаффмана.</param>
    /// <param name="allowedExtensions">
    /// Командні розширення, які приймач розуміє. З <see cref="PayloadFlags.Duplication"/> однакові кадри
    /// (дублі критичних з різних пакетів) зараховуються один раз. Без нього пакет із цим прапорцем → E_PAYLOAD.
    /// </param>
    public Receiver(ICrypto crypto, IHuffmanCodec codec, PayloadFlags allowedExtensions = PayloadFlags.None)
    {
        ArgumentNullException.ThrowIfNull(crypto);
        ArgumentNullException.ThrowIfNull(codec);
        this.crypto = crypto;
        this.codec = codec;
        this.allowedExtensions = allowedExtensions & PayloadFlags.Duplication;
        uniqueFrames = this.allowedExtensions.HasFlag(PayloadFlags.Duplication) ? [] : null;
    }

    /// <summary>Скільки пакетів прийнято (без дублікатів seq).</summary>
    public int Accepted => seenSeq.Count;

    /// <summary>Скільки пакетів відхилено за кожним кодом.</summary>
    public IReadOnlyDictionary<PacketError, int> Rejected => rejected;

    /// <summary>Скільки дублікатів seq проігноровано.</summary>
    public int DuplicateSeq { get; private set; }

    /// <summary>Обробляє один пакет; ніколи не кидає винятків назовні.</summary>
    public OpenResult Accept(byte[] packet)
    {
        OpenResult opened;
        try
        {
            opened = crypto.Open(packet);
        }
        catch (Exception)
        {
            return Reject(OpenResult.Fail(PacketError.E_PAYLOAD));
        }

        if (!opened.Ok)
        {
            return Reject(opened);
        }

        var seq = opened.PacketSeq ?? 0;
        if (seenSeq.Contains(seq))
        {
            DuplicateSeq++;
            return opened; // пакет справжній, але вже врахований: кадри не додаються вдруге
        }

        Payload payload;
        try
        {
            payload = PacketCodec.ReadPayload(opened.Payload!, codec, allowedExtensions);
        }
        catch (PacketFormatException ex)
        {
            return Reject(new OpenResult(ex.Code, seq, null));
        }
        catch (Exception)
        {
            return Reject(new OpenResult(PacketError.E_PAYLOAD, seq, null));
        }

        seenSeq.Add(seq);
        foreach (var f in payload.Frames)
        {
            if (uniqueFrames is null || uniqueFrames.Add(f))
            {
                frames.Add(f);
            }
        }

        sorted = null;
        return opened;
    }

    /// <summary>Усі відновлені кадри, впорядковані за timestamp_ms (стабільно).</summary>
    public IReadOnlyList<Frame> Frames => sorted ??= [.. frames.OrderBy(f => f.TimestampMs)];

    private OpenResult Reject(OpenResult result)
    {
        rejected[result.Error] = rejected.GetValueOrDefault(result.Error) + 1;
        return result;
    }
}
