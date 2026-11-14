using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Telemetry.Security;

/// <summary>
/// Власний AES-256-CTR: keystream = AES-ECB(K_enc, nonce(12) ‖ uint32_BE(i)), i з 1; XOR.
/// AesGcm НЕ використовувати. Власник: Коля.
/// </summary>
public static class CtrCipher
{
    /// <summary>Розмір nonce, байтів.</summary>
    public const int NonceSize = 12;

    private const int BlockSize = 16;

    /// <summary>Шифрування = розшифрування (XOR з keystream).</summary>
    /// <exception cref="ArgumentException">Ключ не 32 байти або nonce не 12 байтів.</exception>
    public static byte[] Transform(byte[] kEnc, byte[] nonce12, byte[] input)
    {
        ArgumentNullException.ThrowIfNull(kEnc);
        ArgumentNullException.ThrowIfNull(nonce12);
        ArgumentNullException.ThrowIfNull(input);
        if (kEnc.Length != Kdf.KeySize)
        {
            throw new ArgumentException("K_enc має бути 32 байти (AES-256).", nameof(kEnc));
        }

        if (nonce12.Length != NonceSize)
        {
            throw new ArgumentException("Nonce має бути 12 байтів.", nameof(nonce12));
        }

        var blocks = (input.Length + BlockSize - 1) / BlockSize;
        var counters = new byte[blocks * BlockSize];
        for (var b = 0; b < blocks; b++)
        {
            var block = counters.AsSpan(b * BlockSize, BlockSize);
            nonce12.CopyTo(block);
            BinaryPrimitives.WriteUInt32BigEndian(block[NonceSize..], checked((uint)(b + 1))); // лічильник з 1
        }

        using var aes = Aes.Create();
        aes.Key = kEnc;

        // ECB тут — лише блоковий примітив для побудови keystream CTR (FORMAT_SPEC §4.2), а не режим шифрування даних.
#pragma warning disable CA5358
        var keystream = aes.EncryptEcb(counters, PaddingMode.None);
#pragma warning restore CA5358

        var output = new byte[input.Length];
        for (var i = 0; i < input.Length; i++)
        {
            output[i] = (byte)(input[i] ^ keystream[i]);
        }

        return output;
    }
}
