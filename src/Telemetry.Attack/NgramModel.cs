namespace Telemetry.Attack;

/// <summary>
/// Модель n-грам мови для пристосованості ГА: log P(текст) = Σ log P(n-грама), ймовірності з корпусу
/// відкритого тексту з додатковим згладжуванням (невідома n-грама має «підлогу» 0,01 / N).
/// </summary>
public sealed class NgramModel
{
    private readonly Dictionary<string, double> logProb = new(StringComparer.Ordinal);
    private readonly double floor;
    private readonly double[] letterFrequency;

    /// <summary>Будує модель за корпусом.</summary>
    /// <param name="corpus">Відкритий текст мови (чим довший, тим краще).</param>
    /// <param name="alphabet">Алфавіт.</param>
    /// <param name="n">Довжина n-грами (2…5).</param>
    /// <exception cref="ArgumentException">Корпус коротший за n літер.</exception>
    public NgramModel(string corpus, Alphabet alphabet, int n = 3)
    {
        ArgumentNullException.ThrowIfNull(corpus);
        ArgumentNullException.ThrowIfNull(alphabet);
        ArgumentOutOfRangeException.ThrowIfLessThan(n, 1);
        Alphabet = alphabet;
        N = n;
        var text = alphabet.Normalize(corpus);
        if (text.Length < n)
        {
            throw new ArgumentException("Корпус коротший за довжину n-грами.", nameof(corpus));
        }

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i + n <= text.Length; i++)
        {
            var g = text.Substring(i, n);
            counts[g] = counts.GetValueOrDefault(g) + 1;
        }

        double total = text.Length - n + 1;
        foreach (var (g, c) in counts)
        {
            logProb[g] = Math.Log10(c / total);
        }

        floor = Math.Log10(0.01 / total);
        letterFrequency = new double[alphabet.Size];
        foreach (var c in text)
        {
            letterFrequency[alphabet.IndexOf(c)]++;
        }

        for (var i = 0; i < letterFrequency.Length; i++)
        {
            letterFrequency[i] /= text.Length;
        }
    }

    /// <summary>Алфавіт моделі.</summary>
    public Alphabet Alphabet { get; }

    /// <summary>Довжина n-грами.</summary>
    public int N { get; }

    /// <summary>Частоти літер корпусу (індекс = номер літери).</summary>
    public IReadOnlyList<double> LetterFrequency => letterFrequency;

    /// <summary>Сумарний log10-правдоподібність літер тексту (більше — схожіше на мову).</summary>
    public double Score(string text)
    {
        var t = Alphabet.Normalize(text);
        var score = 0.0;
        for (var i = 0; i + N <= t.Length; i++)
        {
            score += logProb.TryGetValue(t.Substring(i, N), out var p) ? p : floor;
        }

        return score;
    }

    /// <summary>Середня правдоподібність на n-граму (порівнянна для текстів різної довжини).</summary>
    public double ScorePerGram(string text)
    {
        var grams = Math.Max(1, Alphabet.Normalize(text).Length - N + 1);
        return Score(text) / grams;
    }
}
