using System.Globalization;
using System.Text;
using Telemetry.Core;

namespace Telemetry.Optimize;

/// <summary>Межі генів хромосоми <see cref="Params"/> (базовий рівень: 3 гени).</summary>
/// <param name="MinWindow">Найменше вікно Гаффмана, кадрів.</param>
/// <param name="MaxWindow">Найбільше вікно Гаффмана, кадрів (64 — candidate_window_frames).</param>
/// <param name="MinThreshold">Найменший поріг ентропії для NO_COMPRESSION, біт/байт.</param>
/// <param name="MaxThreshold">Найбільший поріг ентропії, біт/байт.</param>
/// <param name="MinPriorityWeight">Найменша вага пріоритету класу в плануванні.</param>
/// <param name="MaxPriorityWeight">Найбільша вага пріоритету класу в плануванні.</param>
public sealed record GeneBounds(
    int MinWindow = 8,
    int MaxWindow = 64,
    double MinThreshold = 4.0,
    double MaxThreshold = 8.0,
    double MinPriorityWeight = 0.25,
    double MaxPriorityWeight = 4.0)
{
    /// <summary>Приводить параметри в межі генів.</summary>
    public Params Clamp(Params p)
    {
        ArgumentNullException.ThrowIfNull(p);
        return new Params(
            Math.Clamp(p.HuffmanWindowFrames, MinWindow, MaxWindow),
            Math.Clamp(p.NoCompressionThreshold, MinThreshold, MaxThreshold),
            Math.Clamp(p.PriorityWeight, MinPriorityWeight, MaxPriorityWeight));
    }

    /// <summary>Випадкова особина, рівномірно в межах.</summary>
    public Params Random(Random rng)
    {
        ArgumentNullException.ThrowIfNull(rng);
#pragma warning disable CA5394 // не криптографічне використання: пошук параметрів
        return new Params(
            rng.Next(MinWindow, MaxWindow + 1),
            MinThreshold + (rng.NextDouble() * (MaxThreshold - MinThreshold)),
            MinPriorityWeight + (rng.NextDouble() * (MaxPriorityWeight - MinPriorityWeight)));
#pragma warning restore CA5394
    }
}

/// <summary>Налаштування ГА. Кількість обчислень fitness = Population·(Generations + 1) (з кешем — не більше).</summary>
/// <param name="Population">Розмір популяції.</param>
/// <param name="Generations">Поколінь після нульового.</param>
/// <param name="TournamentSize">Розмір турніру.</param>
/// <param name="CrossoverRate">Імовірність кросоверу пари.</param>
/// <param name="MutationRate">Імовірність мутації кожного гена.</param>
/// <param name="Elite">Скільки найкращих переходить без змін (елітизм).</param>
public sealed record GaSettings(
    int Population = 12,
    int Generations = 20,
    int TournamentSize = 3,
    double CrossoverRate = 0.9,
    double MutationRate = 0.25,
    int Elite = 2)
{
    /// <summary>Бюджет обчислень fitness — однаковий для ГА і випадкового пошуку (таблиця 5).</summary>
    public int Evaluations => Population * (Generations + 1);

    /// <summary>Перевіряє налаштування.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Некоректне значення.</exception>
    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(Population, 2);
        ArgumentOutOfRangeException.ThrowIfNegative(Generations);
        ArgumentOutOfRangeException.ThrowIfLessThan(TournamentSize, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(Elite);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(Elite, Population);
        if (CrossoverRate is < 0 or > 1 || MutationRate is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(CrossoverRate), "ймовірності мають бути в [0, 1]");
        }
    }
}

/// <summary>
/// ГА: 3 гени, турнірна селекція, один кросовер, мутація, елітизм. Fitness = Q за кількох seed.
/// Журнал еволюції (покоління, best, mean) — у файл і в репозиторій. Власник: Георгій.
/// </summary>
/// <remarks>
/// Кросовер — одноточковий за трьома генами (точка розрізу 1 або 2). Мутація — гаусова, σ = 15 % ширини
/// діапазону гена, з обрізанням до меж; вікно округлюється до цілого. Елітизм: <see cref="GaSettings.Elite"/>
/// найкращих копіюються в наступне покоління, тому best(покоління g) не спадає (D1 кейс 16).
/// Fitness кешується за параметрами: повторна оцінка тієї самої особини не витрачає бюджет.
/// </remarks>
/// <param name="settings">Налаштування (за замовчуванням 12 × 20).</param>
/// <param name="bounds">Межі генів.</param>
public sealed class GeneticAlgorithm(GaSettings? settings = null, GeneBounds? bounds = null) : IOptimizer
{
    private readonly GaSettings settings = settings ?? new GaSettings();
    private readonly GeneBounds bounds = bounds ?? new GeneBounds();

    /// <summary>Скільки разів fitness реально обчислено в останньому запуску.</summary>
    public int EvaluationsUsed { get; private set; }

    /// <inheritdoc/>
    public (Params Best, EvolutionLog Log) Run(Func<Params, double> fitness, int seed)
    {
        ArgumentNullException.ThrowIfNull(fitness);
        settings.Validate();
#pragma warning disable CA5394 // не криптографічне використання: еволюційний пошук
        var rng = new Random(seed);
        var cache = new Dictionary<Params, double>();
        double Eval(Params p)
        {
            if (!cache.TryGetValue(p, out var f))
            {
                f = fitness(p);
                cache[p] = f;
            }

            return f;
        }

        var population = Enumerable.Range(0, settings.Population).Select(_ => bounds.Random(rng)).ToList();
        var scored = Score(population, Eval);
        var log = new EvolutionLog();
        log.Generations.Add(Record(0, scored));

        for (var g = 1; g <= settings.Generations; g++)
        {
            var next = scored.Take(settings.Elite).Select(s => s.P).ToList();
            while (next.Count < settings.Population)
            {
                var a = Tournament(scored, rng);
                var b = Tournament(scored, rng);
                var (c1, c2) = rng.NextDouble() < settings.CrossoverRate ? Crossover(a, b, rng) : (a, b);
                next.Add(Mutate(c1, rng));
                if (next.Count < settings.Population)
                {
                    next.Add(Mutate(c2, rng));
                }
            }

            scored = Score(next, Eval);
            log.Generations.Add(Record(g, scored));
        }
#pragma warning restore CA5394

        EvaluationsUsed = cache.Count;
        return (scored[0].P, log);
    }

    /// <summary>Журнал еволюції у CSV (generation,best,mean).</summary>
    public static string ToCsv(EvolutionLog log)
    {
        ArgumentNullException.ThrowIfNull(log);
        var sb = new StringBuilder("generation,best,mean\n");
        foreach (var r in log.Generations)
        {
            sb.Append(CultureInfo.InvariantCulture, $"{r.Generation},{r.Best:F6},{r.Mean:F6}\n");
        }

        return sb.ToString();
    }

    private static List<(Params P, double F)> Score(List<Params> population, Func<Params, double> eval) =>
        [.. population.Select(p => (P: p, F: eval(p)))
            .OrderByDescending(s => s.F)
            .ThenBy(s => s.P.HuffmanWindowFrames)
            .ThenBy(s => s.P.NoCompressionThreshold)
            .ThenBy(s => s.P.PriorityWeight)];

    private static GenerationRecord Record(int generation, List<(Params P, double F)> scored) =>
        new(generation, scored[0].F, scored.Average(s => s.F));

#pragma warning disable CA5394 // не криптографічне використання
    private Params Tournament(List<(Params P, double F)> scored, Random rng)
    {
        var best = scored[rng.Next(scored.Count)];
        for (var i = 1; i < settings.TournamentSize; i++)
        {
            var other = scored[rng.Next(scored.Count)];
            if (other.F > best.F)
            {
                best = other;
            }
        }

        return best.P;
    }

    private static (Params, Params) Crossover(Params a, Params b, Random rng)
    {
        // Одноточковий кросовер: гени [вікно | поріг | вага], точка розрізу після 1-го або 2-го гена.
        return rng.Next(1, 3) == 1
            ? (a with { NoCompressionThreshold = b.NoCompressionThreshold, PriorityWeight = b.PriorityWeight },
               b with { NoCompressionThreshold = a.NoCompressionThreshold, PriorityWeight = a.PriorityWeight })
            : (a with { PriorityWeight = b.PriorityWeight }, b with { PriorityWeight = a.PriorityWeight });
    }

    private Params Mutate(Params p, Random rng)
    {
        var window = p.HuffmanWindowFrames;
        var threshold = p.NoCompressionThreshold;
        var weight = p.PriorityWeight;
        if (rng.NextDouble() < settings.MutationRate)
        {
            window = (int)Math.Round(window + (Gaussian(rng) * 0.15 * (bounds.MaxWindow - bounds.MinWindow)));
        }

        if (rng.NextDouble() < settings.MutationRate)
        {
            threshold += Gaussian(rng) * 0.15 * (bounds.MaxThreshold - bounds.MinThreshold);
        }

        if (rng.NextDouble() < settings.MutationRate)
        {
            weight += Gaussian(rng) * 0.15 * (bounds.MaxPriorityWeight - bounds.MinPriorityWeight);
        }

        return bounds.Clamp(new Params(window, threshold, weight));
    }

    private static double Gaussian(Random rng)
    {
        // Бокс — Мюллер.
        var u1 = 1.0 - rng.NextDouble();
        var u2 = rng.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }
#pragma warning restore CA5394
}
