using System.Diagnostics.CodeAnalysis;

namespace Telemetry.Security;

/// <summary>HMAC-SHA256 над заголовком + шифротекстом (Encrypt-then-MAC). Власник: Коля.</summary>
[ExcludeFromCodeCoverage(Justification = "Заглушка каркаса; прибрати атрибут разом з реалізацією")]
public static class Mac
{
    /// <summary>Розмір тега, байтів.</summary>
    public const int TagSize = 32;

    /// <summary>Обчислює тег.</summary>
    public static byte[] Compute(byte[] kMac, ReadOnlySpan<byte> headerAndCiphertext) => throw new NotImplementedException();

    /// <summary>Порівняння в постійний час (CryptographicOperations.FixedTimeEquals).</summary>
    public static bool Verify(byte[] kMac, ReadOnlySpan<byte> headerAndCiphertext, ReadOnlySpan<byte> tag) =>
        throw new NotImplementedException();
}
