using System.Diagnostics.CodeAnalysis;
using Telemetry.Core;

namespace Telemetry.Optimize;

/// <summary>Випадковий пошук з тією самою кількістю обчислень fitness, що й ГА (таблиця 5). Власник: Георгій.</summary>
[ExcludeFromCodeCoverage(Justification = "Заглушка каркаса; прибрати атрибут разом з реалізацією")]
public sealed class RandomSearch : IOptimizer
{
    /// <inheritdoc/>
    public (Params Best, EvolutionLog Log) Run(Func<Params, double> fitness, int seed) =>
        throw new NotImplementedException();
}
