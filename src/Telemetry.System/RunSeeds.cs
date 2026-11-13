using System.Globalization;

namespace Telemetry.System;

/// <summary>
/// Seed каналу для повного прогону (пастка 12). Відкритий seed лише для налагодження, тому міряємо на п'яти:
/// відкритий і чотири похідні (seed + 1 … seed + 4). Список можна задати явно (<c>--seeds</c>), наприклад
/// приховані seed викладача. Власник: тімлід.
/// </summary>
public static class RunSeeds
{
    /// <summary>Скільки seed у стандартному прогоні.</summary>
    public const int DefaultCount = 5;

    /// <summary>Відкритий seed і чотири похідні.</summary>
    public static IReadOnlyList<int> Default(int openSeed) =>
        [.. Enumerable.Range(0, DefaultCount).Select(k => checked(openSeed + k))];

    /// <summary>Розбирає список seed через кому: цілі, без повторів, не порожній.</summary>
    /// <exception cref="FormatException">Не ціле число, повтор або порожній список.</exception>
    public static IReadOnlyList<int> Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var seeds = new List<int>();
        foreach (var part in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seed))
            {
                throw new FormatException($"seed «{part}» не є цілим числом");
            }

            if (seeds.Contains(seed))
            {
                throw new FormatException($"seed {seed} повторюється");
            }

            seeds.Add(seed);
        }

        return seeds.Count > 0 ? seeds : throw new FormatException("список seed порожній");
    }
}
