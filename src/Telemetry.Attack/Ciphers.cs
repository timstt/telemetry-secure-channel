using System.Text;

namespace Telemetry.Attack;

/// <summary>
/// Алфавіт слабкого шифру. Символи поза алфавітом (пробіли, розділові знаки) проходять без змін, регістр
/// зберігається. Модуль атаки (PROJECT_1 §3.7, тиждень 12).
/// </summary>
public sealed class Alphabet
{
    private readonly Dictionary<char, int> index = [];

    /// <summary>Створює алфавіт із рядка малих літер без повторів.</summary>
    /// <exception cref="ArgumentException">Порожній рядок або повтори.</exception>
    public Alphabet(string lowerLetters)
    {
        ArgumentException.ThrowIfNullOrEmpty(lowerLetters);
        Letters = lowerLetters;
        for (var i = 0; i < lowerLetters.Length; i++)
        {
            if (!index.TryAdd(lowerLetters[i], i))
            {
                throw new ArgumentException("Літери алфавіту повторюються.", nameof(lowerLetters));
            }
        }
    }

    /// <summary>Англійський алфавіт (26).</summary>
    public static Alphabet English { get; } = new("abcdefghijklmnopqrstuvwxyz");

    /// <summary>Український алфавіт (33).</summary>
    public static Alphabet Ukrainian { get; } = new("абвгґдеєжзиіїйклмнопрстуфхцчшщьюя");

    /// <summary>Літери за порядком.</summary>
    public string Letters { get; }

    /// <summary>Розмір алфавіту.</summary>
    public int Size => Letters.Length;

    /// <summary>Номер літери (без урахування регістру) або −1.</summary>
    public int IndexOf(char c) => index.TryGetValue(char.ToLowerInvariant(c), out var i) ? i : -1;

    /// <summary>Літера з номером <paramref name="i"/> у регістрі зразка <paramref name="caseOf"/>.</summary>
    public char At(int i, char caseOf) => char.IsUpper(caseOf) ? char.ToUpperInvariant(Letters[i]) : Letters[i];

    /// <summary>Лише літери алфавіту тексту (малими), для статистики.</summary>
    public string Normalize(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            var i = IndexOf(c);
            if (i >= 0)
            {
                sb.Append(Letters[i]);
            }
        }

        return sb.ToString();
    }
}

/// <summary>Шифр Віженера: зсув кожної літери на літеру ключа (ключ рухається лише по літерах).</summary>
public static class Vigenere
{
    /// <summary>Шифрує.</summary>
    public static string Encrypt(string text, int[] key, Alphabet alphabet) => Shift(text, key, alphabet, +1);

    /// <summary>Розшифровує.</summary>
    public static string Decrypt(string text, int[] key, Alphabet alphabet) => Shift(text, key, alphabet, -1);

    /// <summary>Ключ із рядка літер.</summary>
    /// <exception cref="ArgumentException">Порожній ключ або літера поза алфавітом.</exception>
    public static int[] Key(string key, Alphabet alphabet)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(alphabet);
        var k = key.Select(alphabet.IndexOf).ToArray();
        return k.Length > 0 && k.All(i => i >= 0) ? k : throw new ArgumentException("Ключ має складатися з літер алфавіту.", nameof(key));
    }

    private static string Shift(string text, int[] key, Alphabet alphabet, int sign)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(alphabet);
        if (key.Length == 0)
        {
            throw new ArgumentException("Порожній ключ.", nameof(key));
        }

        var sb = new StringBuilder(text.Length);
        var j = 0;
        foreach (var c in text)
        {
            var i = alphabet.IndexOf(c);
            if (i < 0)
            {
                sb.Append(c);
                continue;
            }

            var shifted = ((i + (sign * key[j % key.Length])) % alphabet.Size + alphabet.Size) % alphabet.Size;
            sb.Append(alphabet.At(shifted, c));
            j++;
        }

        return sb.ToString();
    }
}

/// <summary>Моноалфавітна підстановка: key[p] — літера шифротексту для літери відкритого тексту p.</summary>
public static class Substitution
{
    /// <summary>Шифрує.</summary>
    public static string Encrypt(string text, int[] key, Alphabet alphabet) => Map(text, key, alphabet);

    /// <summary>Розшифровує.</summary>
    public static string Decrypt(string text, int[] key, Alphabet alphabet) => Map(text, Inverse(key), alphabet);

    /// <summary>Обернена перестановка.</summary>
    /// <exception cref="ArgumentException">Ключ не є перестановкою.</exception>
    public static int[] Inverse(int[] key)
    {
        ArgumentNullException.ThrowIfNull(key);
        var inv = Enumerable.Repeat(-1, key.Length).ToArray();
        for (var i = 0; i < key.Length; i++)
        {
            if (key[i] < 0 || key[i] >= key.Length || inv[key[i]] >= 0)
            {
                throw new ArgumentException("Ключ підстановки має бути перестановкою.", nameof(key));
            }

            inv[key[i]] = i;
        }

        return inv;
    }

    private static string Map(string text, int[] key, Alphabet alphabet)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(alphabet);
        if (key.Length != alphabet.Size)
        {
            throw new ArgumentException("Довжина ключа ≠ розміру алфавіту.", nameof(key));
        }

        _ = Inverse(key);
        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            var i = alphabet.IndexOf(c);
            sb.Append(i < 0 ? c : alphabet.At(key[i], c));
        }

        return sb.ToString();
    }
}

/// <summary>XOR із коротким ключем, що повторюється (байти).</summary>
public static class XorCipher
{
    /// <summary>Шифрування = розшифрування.</summary>
    /// <exception cref="ArgumentException">Порожній ключ.</exception>
    public static byte[] Apply(byte[] data, byte[] key)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(key);
        if (key.Length == 0)
        {
            throw new ArgumentException("Порожній ключ.", nameof(key));
        }

        var result = new byte[data.Length];
        for (var i = 0; i < data.Length; i++)
        {
            result[i] = (byte)(data[i] ^ key[i % key.Length]);
        }

        return result;
    }
}
