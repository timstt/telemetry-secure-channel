using System.Text;

namespace Telemetry.Attack;

/// <summary>Налаштування ГА пошуку ключа.</summary>
/// <param name="Population">Розмір популяції.</param>
/// <param name="Generations">Поколінь.</param>
/// <param name="TournamentSize">Розмір турніру.</param>
/// <param name="MutationRate">Імовірність мутації гена (для перестановки — імовірність кожного обміну).</param>
/// <param name="Elite">Скільки найкращих переходить без змін.</param>
/// <param name="Seed">Seed генератора.</param>
public sealed record KeyGaSettings(int Population = 60, int Generations = 150, int TournamentSize = 3, double MutationRate = 0.15, int Elite = 4, int Seed = 1);

/// <summary>Результат атаки.</summary>
/// <param name="Key">Знайдений ключ (номери літер або байти).</param>
/// <param name="Plaintext">Розшифрований текст.</param>
/// <param name="Fitness">Пристосованість знайденого ключа.</param>
/// <param name="BestByGeneration">Найкраща пристосованість за поколіннями (журнал збіжності).</param>
public sealed record AttackResult(int[] Key, string Plaintext, double Fitness, IReadOnlyList<double> BestByGeneration);

/// <summary>
/// Криптоаналіз слабких шифрів генетичним алгоритмом (PROJECT_1 §3.7 п.1): пристосованість — правдоподібність
/// n-грам розшифрованого тексту. Турнірна селекція, кросовер (одноточковий для векторів, OX для перестановок),
/// мутація, елітизм; для підстановки — меметичний крок (локальні обміни найкращої особини). AES не атакується.
/// Довжина ключа Віженера/XOR оцінюється за індексом збігу.
/// </summary>
public static class KeySearch
{
    /// <summary>Імовірні довжини ключа за індексом збігу стовпців (найкращі першими).</summary>
    public static int[] LikelyKeyLengths(IReadOnlyList<int> symbols, int maxLength, int take = 3)
    {
        ArgumentNullException.ThrowIfNull(symbols);
        var scored = new List<(int Length, double Ic)>();
        for (var len = 1; len <= Math.Min(maxLength, Math.Max(1, symbols.Count / 2)); len++)
        {
            var ic = 0.0;
            for (var c = 0; c < len; c++)
            {
                var counts = new Dictionary<int, int>();
                var n = 0;
                for (var i = c; i < symbols.Count; i += len)
                {
                    counts[symbols[i]] = counts.GetValueOrDefault(symbols[i]) + 1;
                    n++;
                }

                ic += n > 1 ? counts.Values.Sum(v => (double)v * (v - 1)) / (n * (n - 1.0)) : 0;
            }

            scored.Add((len, ic / len));
        }

        // Кратні справжньої довжини теж мають високий IC — за майже рівного IC беремо коротшу.
        var best = scored.Max(s => s.Ic);
        return [.. scored.OrderByDescending(s => s.Ic >= 0.9 * best ? 1 : 0).ThenBy(s => s.Ic >= 0.9 * best ? s.Length : 0).ThenByDescending(s => s.Ic).Take(take).Select(s => s.Length)];
    }

    /// <summary>Атака на Віженера: оцінка довжини ключа, далі ГА по літерах ключа.</summary>
    public static AttackResult BreakVigenere(string ciphertext, NgramModel model, int maxKeyLength = 12, KeyGaSettings? settings = null)
    {
        ArgumentNullException.ThrowIfNull(ciphertext);
        ArgumentNullException.ThrowIfNull(model);
        settings ??= new KeyGaSettings();
        var a = model.Alphabet;
        var symbols = a.Normalize(ciphertext).Select(a.IndexOf).ToArray();
        AttackResult? best = null;
        foreach (var len in LikelyKeyLengths(symbols, maxKeyLength))
        {
            var r = Evolve(len, a.Size, permutation: false, k => model.Score(Vigenere.Decrypt(ciphertext, k, a)), settings, null);
            if (best is null || r.Fitness > best.Fitness)
            {
                best = r with { Plaintext = Vigenere.Decrypt(ciphertext, r.Key, a) };
            }
        }

        return best!;
    }

    /// <summary>Атака на моноалфавітну підстановку: ГА по перестановках, початкова популяція — частотний аналіз.</summary>
    public static AttackResult BreakSubstitution(string ciphertext, NgramModel model, KeyGaSettings? settings = null)
    {
        ArgumentNullException.ThrowIfNull(ciphertext);
        ArgumentNullException.ThrowIfNull(model);
        settings ??= new KeyGaSettings(Population: 60, Generations: 150, MutationRate: 0.05);
        var a = model.Alphabet;

        // Частотне припущення: k-та за частотою літера відкритого тексту → k-та за частотою літера шифротексту.
        var cipherFreq = new double[a.Size];
        foreach (var c in a.Normalize(ciphertext))
        {
            cipherFreq[a.IndexOf(c)]++;
        }

        var plainOrder = Enumerable.Range(0, a.Size).OrderByDescending(i => model.LetterFrequency[i]).ThenBy(i => i).ToArray();
        var cipherOrder = Enumerable.Range(0, a.Size).OrderByDescending(i => cipherFreq[i]).ThenBy(i => i).ToArray();
        var guess = new int[a.Size];
        for (var k = 0; k < a.Size; k++)
        {
            guess[plainOrder[k]] = cipherOrder[k];
        }

        var r = Evolve(a.Size, a.Size, permutation: true, k => model.Score(Substitution.Decrypt(ciphertext, k, a)), settings, guess);
        return r with { Plaintext = Substitution.Decrypt(ciphertext, r.Key, a) };
    }

    /// <summary>Атака на XOR із коротким ключем: довжина за індексом збігу байтів, ГА по байтах ключа.</summary>
    public static AttackResult BreakXor(byte[] ciphertext, NgramModel model, int maxKeyLength = 8, KeyGaSettings? settings = null)
    {
        ArgumentNullException.ThrowIfNull(ciphertext);
        ArgumentNullException.ThrowIfNull(model);
        settings ??= new KeyGaSettings();
        AttackResult? best = null;
        foreach (var len in LikelyKeyLengths(ciphertext.Select(b => (int)b).ToArray(), maxKeyLength))
        {
            var r = Evolve(len, 256, permutation: false, k => XorFitness(ciphertext, k, model), settings, null);
            if (best is null || r.Fitness > best.Fitness)
            {
                best = r with { Plaintext = Encoding.UTF8.GetString(XorCipher.Apply(ciphertext, [.. r.Key.Select(b => (byte)b)])) };
            }
        }

        return best!;
    }

    private static double XorFitness(byte[] ciphertext, int[] key, NgramModel model)
    {
        var plain = XorCipher.Apply(ciphertext, [.. key.Select(b => (byte)b)]);
        if (plain.Length == 0)
        {
            return 0;
        }

        int unprintable = 0, textual = 0;
        var sb = new StringBuilder(plain.Length);
        foreach (var b in plain)
        {
            var c = (char)b;
            if (b is (< 32 or > 126) and not (10 or 13))
            {
                unprintable++;
            }
            else if (c == ' ' || model.Alphabet.IndexOf(c) >= 0)
            {
                textual++;
            }

            sb.Append(c);
        }

        // Середня правдоподібність n-грам не залежить від кількості літер (інакше «менше літер» вигравало б),
        // частка літер і пробілів відрізняє текст від друкованого сміття, недруковані байти караються окремо.
        return model.ScorePerGram(sb.ToString()) + (5.0 * textual / plain.Length) - (10.0 * unprintable / plain.Length);
    }

    private static AttackResult Evolve(int length, int range, bool permutation, Func<int[], double> fitness, KeyGaSettings s, int[]? seedIndividual)
    {
#pragma warning disable CA5394 // не криптографічне використання: еволюційний пошук ключа слабкого шифру
        var rng = new Random(s.Seed);
        int[] RandomKey()
        {
            if (!permutation)
            {
                return [.. Enumerable.Range(0, length).Select(_ => rng.Next(range))];
            }

            var p = Enumerable.Range(0, length).ToArray();
            rng.Shuffle(p);
            return p;
        }

        var population = new List<int[]>();
        if (seedIndividual is not null)
        {
            population.Add(seedIndividual);
            while (population.Count < s.Population / 2)
            {
                population.Add(Mutate((int[])seedIndividual.Clone(), 0.1));
            }
        }

        while (population.Count < s.Population)
        {
            population.Add(RandomKey());
        }

        var scored = population.Select(k => (Key: k, F: fitness(k))).OrderByDescending(x => x.F).ToList();
        var history = new List<double> { scored[0].F };
        for (var g = 0; g < s.Generations; g++)
        {
            var next = scored.Take(s.Elite).Select(x => x.Key).ToList();
            while (next.Count < s.Population)
            {
                var a = Tournament(scored);
                var b = Tournament(scored);
                next.Add(Mutate(permutation ? OrderCrossover(a, b) : OnePoint(a, b), s.MutationRate));
            }

            scored = [.. next.Select(k => (Key: k, F: fitness(k))).OrderByDescending(x => x.F)];
            if (permutation)
            {
                // Меметичний крок: локальне покращення найкращої особини обмінами (перший кращий обмін).
                scored[0] = Improve(scored[0]);
                scored = [.. scored.OrderByDescending(x => x.F)];
            }

            history.Add(scored[0].F);
        }

        return new AttackResult(scored[0].Key, "", scored[0].F, history);

        (int[] Key, double F) Improve((int[] Key, double F) individual)
        {
            var key = (int[])individual.Key.Clone();
            var f = individual.F;
            for (var x = 0; x < length; x++)
            {
                for (var y = x + 1; y < length; y++)
                {
                    (key[x], key[y]) = (key[y], key[x]);
                    var candidate = fitness(key);
                    if (candidate > f)
                    {
                        f = candidate;
                    }
                    else
                    {
                        (key[x], key[y]) = (key[y], key[x]);
                    }
                }
            }

            return (key, f);
        }

        int[] Tournament(List<(int[] Key, double F)> pool)
        {
            var best = pool[rng.Next(pool.Count)];
            for (var i = 1; i < s.TournamentSize; i++)
            {
                var other = pool[rng.Next(pool.Count)];
                if (other.F > best.F)
                {
                    best = other;
                }
            }

            return best.Key;
        }

        int[] OnePoint(int[] a, int[] b)
        {
            var cut = length > 1 ? rng.Next(1, length) : 0;
            return [.. a.Take(cut), .. b.Skip(cut)];
        }

        int[] OrderCrossover(int[] a, int[] b)
        {
            // OX: відрізок від батька a, решта генів у порядку батька b — нащадок лишається перестановкою.
            var i = rng.Next(length);
            var j = rng.Next(length);
            (i, j) = (Math.Min(i, j), Math.Max(i, j));
            var child = Enumerable.Repeat(-1, length).ToArray();
            var used = new bool[length];
            for (var k = i; k <= j; k++)
            {
                child[k] = a[k];
                used[a[k]] = true;
            }

            var pos = 0;
            foreach (var gene in b)
            {
                if (used[gene])
                {
                    continue;
                }

                while (child[pos] >= 0)
                {
                    pos++;
                }

                child[pos] = gene;
            }

            return child;
        }

        int[] Mutate(int[] key, double rate)
        {
            if (permutation)
            {
                // Принаймні один обмін, далі ще з імовірністю rate кожен.
                do
                {
                    var x = rng.Next(length);
                    var y = rng.Next(length);
                    (key[x], key[y]) = (key[y], key[x]);
                }
                while (rng.NextDouble() < rate * length / 4);
                return key;
            }

            for (var k = 0; k < key.Length; k++)
            {
                if (rng.NextDouble() < rate)
                {
                    key[k] = rng.Next(range);
                }
            }

            return key;
        }
#pragma warning restore CA5394
    }
}
