using System.Diagnostics.CodeAnalysis;
using Telemetry.Core;

namespace Telemetry.Optimize;

/// <summary>
/// ГА: 3 гени, турнірна селекція, один кросовер, мутація, елітизм. Fitness = Q за кількох seed.
/// Журнал еволюції (покоління, best, mean) — у файл і в репозиторій. Власник: Георгій.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Заглушка каркаса; прибрати атрибут разом з реалізацією")]
public sealed class GeneticAlgorithm : IOptimizer
{
    /// <inheritdoc/>
    public (Params Best, EvolutionLog Log) Run(Func<Params, double> fitness, int seed) =>
        throw new NotImplementedException();
}
