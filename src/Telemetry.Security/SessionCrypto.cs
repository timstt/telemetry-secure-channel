using System.Buffers.Binary;
using Telemetry.Core;

namespace Telemetry.Security;

/// <summary>
/// <see cref="ICrypto"/> сеансу: KDF з кешем, AES-256-CTR, HMAC-SHA256 (Encrypt-then-MAC).
/// <see cref="Open"/> виконує кроки 1–6 FORMAT_SPEC §5 строго по порядку і ніколи не кидає винятків:
/// HMAC перевіряється в постійний час ДО розшифрування, а при відмові вміст не розшифровується.
/// Власник: Коля.
/// </summary>
public sealed class SessionCrypto : ICrypto
{
    /// <summary>Розмір відкритого заголовка.</summary>
    public const int HeaderSize = 43;

    /// <summary>Мінімальна довжина пакета: 43 + 0 + 32.</summary>
    public const int MinPacketLength = HeaderSize + Mac.TagSize;

    /// <summary>Версія формату.</summary>
    public const byte Version = 1;

    private const int SeqOffset = 7;
    private const int SaltOffset = 11;
    private const int NonceOffset = 27;
    private const int LengthOffset = 39;

    private static readonly byte[] Magic = "TLM1"u8.ToArray();

    private readonly string password;
    private readonly Kdf kdf;
    private readonly Func<byte[], byte[], byte[], byte[]> ctr;

    /// <summary>Криптографія для пароля сеансу.</summary>
    /// <param name="password">Пароль сеансу (не логується).</param>
    /// <param name="kdf">Спільний кеш KDF (за замовчуванням власний).</param>
    public SessionCrypto(string password, Kdf? kdf = null)
        : this(password, kdf, CtrCipher.Transform)
    {
    }

    /// <summary>Конструктор із підмінним CTR — для тестів порядку перевірок (C5 п.1–2).</summary>
    /// <param name="password">Пароль сеансу.</param>
    /// <param name="kdf">Кеш KDF.</param>
    /// <param name="ctr">Перетворення CTR (K_enc, nonce, дані).</param>
    public SessionCrypto(string password, Kdf? kdf, Func<byte[], byte[], byte[], byte[]> ctr)
    {
        ArgumentNullException.ThrowIfNull(password);
        ArgumentNullException.ThrowIfNull(ctr);
        this.password = password;
        this.kdf = kdf ?? new Kdf();
        this.ctr = ctr;
    }

    /// <summary>Кеш KDF цієї криптографії.</summary>
    public Kdf Kdf => kdf;

    /// <inheritdoc/>
    public (byte[] KEnc, byte[] KMac) DeriveKeys(string password, byte[] salt) => kdf.Derive(password, salt);

    /// <inheritdoc/>
    /// <exception cref="ArgumentException">Заголовок не 43 байти або ciphertext_len у ньому ≠ довжині навантаження.</exception>
    public byte[] Seal(byte[] header, byte[] payload)
    {
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(payload);
        if (header.Length != HeaderSize)
        {
            throw new ArgumentException("Заголовок має бути 43 байти.", nameof(header));
        }

        if (BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(LengthOffset)) != (uint)payload.Length)
        {
            throw new ArgumentException("ciphertext_len у заголовку не дорівнює довжині навантаження.", nameof(header));
        }

        var (kEnc, kMac) = kdf.Derive(password, header[SaltOffset..NonceOffset]);
        var ciphertext = ctr(kEnc, header[NonceOffset..LengthOffset], payload);

        var packet = new byte[HeaderSize + ciphertext.Length + Mac.TagSize];
        header.CopyTo(packet, 0);
        ciphertext.CopyTo(packet, HeaderSize);
        var tag = Mac.Compute(kMac, packet.AsSpan(0, HeaderSize + ciphertext.Length));
        tag.CopyTo(packet, HeaderSize + ciphertext.Length);
        return packet;
    }

    /// <inheritdoc/>
    public OpenResult Open(byte[] packet)
    {
        if (packet is null || packet.Length < MinPacketLength)
        {
            return OpenResult.Fail(PacketError.E_SHORT);                       // крок 1
        }

        if (!packet.AsSpan(0, Magic.Length).SequenceEqual(Magic) || packet[Magic.Length] != Version)
        {
            return OpenResult.Fail(PacketError.E_MAGIC);                       // крок 2
        }

        var seq = BinaryPrimitives.ReadUInt32BigEndian(packet.AsSpan(SeqOffset));
        long ciphertextLength = BinaryPrimitives.ReadUInt32BigEndian(packet.AsSpan(LengthOffset));
        if (packet.LongLength != HeaderSize + ciphertextLength + Mac.TagSize)
        {
            return new OpenResult(PacketError.E_LENGTH, seq, null);            // крок 3
        }

        var covered = HeaderSize + (int)ciphertextLength;
        var (kEnc, kMac) = kdf.Derive(password, packet[SaltOffset..NonceOffset]);
        if (!Mac.Verify(kMac, packet.AsSpan(0, covered), packet.AsSpan(covered)))
        {
            return new OpenResult(PacketError.E_HMAC, seq, null);              // кроки 4–5: не розшифровувати
        }

        var plaintext = ctr(kEnc, packet[NonceOffset..LengthOffset], packet[HeaderSize..covered]); // крок 6
        return new OpenResult(PacketError.None, seq, plaintext);
    }
}
