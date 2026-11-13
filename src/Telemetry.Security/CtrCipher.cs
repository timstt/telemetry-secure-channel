using System.Diagnostics.CodeAnalysis;

namespace Telemetry.Security;

/// <summary>
/// Власний AES-256-CTR: keystream = AES-ECB(K_enc, nonce(12) ‖ uint32_BE(i)), i з 1; XOR.
/// AesGcm НЕ використовувати. Власник: Коля.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Заглушка каркаса; прибрати атрибут разом з реалізацією")]
public static class CtrCipher
{
    /// <summary>Шифрування = розшифрування (XOR з keystream).</summary>
    public static byte[] Transform(byte[] kEnc, byte[] nonce12, byte[] input) => throw new NotImplementedException();
}
