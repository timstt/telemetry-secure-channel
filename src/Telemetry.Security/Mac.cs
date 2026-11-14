using System.Security.Cryptography;

namespace Telemetry.Security;

/// <summary>HMAC-SHA256 над заголовком + шифротекстом (Encrypt-then-MAC). Власник: Коля.</summary>
public static class Mac
{
    /// <summary>Розмір тега, байтів.</summary>
    public const int TagSize = 32;

    /// <summary>Обчислює тег.</summary>
    public static byte[] Compute(byte[] kMac, ReadOnlySpan<byte> headerAndCiphertext)
    {
        ArgumentNullException.ThrowIfNull(kMac);
        return HMACSHA256.HashData(kMac, headerAndCiphertext);
    }

    /// <summary>Порівняння в постійний час (CryptographicOperations.FixedTimeEquals).</summary>
    public static bool Verify(byte[] kMac, ReadOnlySpan<byte> headerAndCiphertext, ReadOnlySpan<byte> tag)
    {
        ArgumentNullException.ThrowIfNull(kMac);
        Span<byte> expected = stackalloc byte[TagSize];
        HMACSHA256.HashData(kMac, headerAndCiphertext, expected);

        // FixedTimeEquals повертає false і для різної довжини — тег іншої довжини просто не збігається.
        return CryptographicOperations.FixedTimeEquals(expected, tag);
    }
}
