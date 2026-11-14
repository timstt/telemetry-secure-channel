using System.Security.Cryptography;
using System.Text;

namespace Telemetry.Security;

/// <summary>
/// PBKDF2-HMAC-SHA256 (Rfc2898DeriveBytes.Pbkdf2): UTF-8 пароль, сіль 16 Б, 100000 ітерацій, 64 Б.
/// K_enc = [0..32), K_mac = [32..64). Кеш на сеанс — не на пакет. Ключі не логувати. Власник: Коля.
/// </summary>
public sealed class Kdf
{
    /// <summary>Кількість ітерацій (FORMAT_SPEC §4.1).</summary>
    public const int Iterations = 100_000;

    /// <summary>Довжина солі, байтів.</summary>
    public const int SaltSize = 16;

    /// <summary>Довжина кожного з ключів, байтів.</summary>
    public const int KeySize = 32;

    private readonly Dictionary<string, (byte[] KEnc, byte[] KMac)> cache = new(StringComparer.Ordinal);
    private readonly object gate = new();

    /// <summary>Скільки разів реально виконано PBKDF2 (для тесту кешу; кожен виклик — 100000 ітерацій).</summary>
    public int Derivations { get; private set; }

    /// <summary>Виводить (K_enc, K_mac) або повертає закешовані для тієї ж пари (пароль, сіль).</summary>
    /// <exception cref="ArgumentException">Сіль не 16 байтів.</exception>
    public (byte[] KEnc, byte[] KMac) Derive(string password, byte[] salt)
    {
        ArgumentNullException.ThrowIfNull(password);
        ArgumentNullException.ThrowIfNull(salt);
        if (salt.Length != SaltSize)
        {
            throw new ArgumentException("Сіль має бути 16 байтів.", nameof(salt));
        }

        // Ключ кешу містить пароль лише в пам'яті процесу; ні ключ кешу, ні значення не логуються.
        var cacheKey = Convert.ToHexString(salt) + "\u0000" + password;
        lock (gate)
        {
            if (cache.TryGetValue(cacheKey, out var keys))
            {
                return keys;
            }

            var master = Rfc2898DeriveBytes.Pbkdf2(
                Encoding.UTF8.GetBytes(password), salt, Iterations, HashAlgorithmName.SHA256, 2 * KeySize);
            keys = (master[..KeySize], master[KeySize..]);
            CryptographicOperations.ZeroMemory(master);
            cache[cacheKey] = keys;
            Derivations++;
            return keys;
        }
    }
}
