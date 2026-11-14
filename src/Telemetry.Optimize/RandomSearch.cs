using Telemetry.Core;

namespace Telemetry.Optimize;

/// <summary>Випадковий пошук з тією самою кількістю обчислень fitness, що й ГА (таблиця 5). Власник: Георгій.</summary>
/// <remarks>
/// Оцінює <see cref="GaSettings.Evaluations"/> випадкових особин, розбитих на «покоління» по
/// <see cref="GaSettings.Population"/>, щоб журнал був порівнянний з ГА: Best — найкраще знайдене до цього
/// покоління включно, Mean — середнє в цьому поколінні.
/// </remarks>
/// <param name="settings">Ті самі налаштування, що й у ГА (бюджет обчислень).</param>
/// <param name="bounds">Межі генів.</param>
public sealed class RandomSearch(GaSettings? settings = null, GeneBounds? bounds = null) : IOptimizer
{
    private readonly GaSettings settings = settings ?? new GaSettings();
    private readonly GeneBounds bounds = bounds ?? new GeneBounds();

    /// <inheritdoc/>
    public (Params Best, EvolutionLog Log) Run(Func<Params, double> fitness, int seed)
    {
        ArgumentNullException.ThrowIfNull(fitness);
        settings.Validate();
#pragma warning disable CA5394 // не криптографічне використання
        var rng = new Random(seed);
#pragma warning restore CA5394
        var log = new EvolutionLog();
        Params? best = null;
        var bestF = double.NegativeInfinity;
        for (var g = 0; g <= settings.Generations; g++)
        {
            var sum = 0.0;
            for (var i = 0; i < settings.Population; i++)
            {
                var p = bounds.Random(rng);
                var f = fitness(p);
                sum += f;
                if (f > bestF)
                {
                    bestF = f;
                    best = p;
                }
            }

            log.Generations.Add(new GenerationRecord(g, bestF, sum / settings.Population));
        }

        return (best!, log);
    }
}
