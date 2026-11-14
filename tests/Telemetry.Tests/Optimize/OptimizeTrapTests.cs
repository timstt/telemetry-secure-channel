using Telemetry.Core;
using Telemetry.Optimize;

namespace Telemetry.Tests.Optimize;

/// <summary>Елітизм ГА (D1 кейс 16) — зона Георгія.</summary>
public class OptimizeTrapTests
{
    [Fact]
    public void Elitism_best_never_decreases()
    {
        static double Fitness(Params p) => -Math.Abs(p.HuffmanWindowFrames - 64) - Math.Abs(p.NoCompressionThreshold - 7);
        var (_, log) = new GeneticAlgorithm().Run(Fitness, seed: 1);
        Assert.NotEmpty(log.Generations);
        Assert.True(log.Generations[^1].Best >= log.Generations[0].Best);
    }
}
