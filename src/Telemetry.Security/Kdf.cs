using System.Diagnostics.CodeAnalysis;

namespace Telemetry.Security;

/// <summary>
/// PBKDF2-HMAC-SHA256 (Rfc2898DeriveBytes.Pbkdf2): UTF-8 пароль, сіль 16 Б, 100000 ітерацій, 64 Б.
/// K_enc = [0..32), K_mac = [32..64). Кеш на сеанс — не на пакет. Ключі не логувати. Власник: Коля.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Заглушка каркаса; прибрати атрибут разом з реалізацією")]
public sealed class Kdf
{
    /// <summary>Кількість ітерацій (FORMAT_SPEC §4.1).</summary>
    public const int Iterations = 100_000;

    /// <summary>Виводить (K_enc, K_mac) або повертає закешовані для тієї ж пари (пароль, сіль).</summary>
    public (byte[] KEnc, byte[] KMac) Derive(string password, byte[] salt) => throw new NotImplementedException();
}
