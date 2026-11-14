using Telemetry.Core;
using Telemetry.Optimize;
using Telemetry.Packing;

namespace Telemetry.Tests.Optimize;

/// <summary>ДП-рюкзак, ГА і випадковий пошук (зона Георгія).</summary>
public class OptimizerTests
{
    private static double Peak(Params p) =>
        -Math.Abs(p.HuffmanWindowFrames - 32) - (4 * Math.Abs(p.NoCompressionThreshold - 7)) - Math.Abs(p.PriorityWeight - 1.5);

    [Fact]
    public void Dp_prefers_lighter_solution_among_equal_values_and_takes_zero_weight_items()
    {
        var dp = new KnapsackDp();
        Assert.Equal(new[] { 1 }, dp.Select([10, 5], [3, 3], 10));
        Assert.Equal(new[] { 0, 1 }, dp.Select([0, 4], [2, 3], 4));
        Assert.Equal(new[] { 0 }, dp.Select([0, 4], [2, 3], 0));
        Assert.Empty(dp.Select([-1, 3], [5, 0], 10));
        Assert.Throws<ArgumentException>(() => dp.Select([1], [], 10));
    }

    [Fact]
    public void Ga_is_deterministic_by_seed_and_beats_initial_population()
    {
        var ga = new GeneticAlgorithm(new GaSettings(Population: 16, Generations: 25));
        var (best, log) = ga.Run(Peak, seed: 7);
        var (again, _) = new GeneticAlgorithm(new GaSettings(Population: 16, Generations: 25)).Run(Peak, seed: 7);
        Assert.Equal(best, again);
        Assert.Equal(26, log.Generations.Count);
        for (var g = 1; g < log.Generations.Count; g++)
        {
            Assert.True(log.Generations[g].Best >= log.Generations[g - 1].Best); // елітизм
        }

        Assert.True(log.Generations[^1].Best > log.Generations[0].Best);
        Assert.InRange(best.HuffmanWindowFrames, 26, 38);
        Assert.InRange(ga.EvaluationsUsed, 1, new GaSettings(Population: 16, Generations: 25).Evaluations);
    }

    [Fact]
    public void Random_search_uses_same_budget_and_best_never_decreases()
    {
        var settings = new GaSettings(Population: 10, Generations: 9);
        var calls = 0;
        var (best, log) = new RandomSearch(settings).Run(p => { calls++; return Peak(p); }, seed: 3);
        Assert.Equal(settings.Evaluations, calls);
        Assert.Equal(10, log.Generations.Count);
        Assert.Equal(log.Generations[^1].Best, Peak(best));
        for (var g = 1; g < log.Generations.Count; g++)
        {
            Assert.True(log.Generations[g].Best >= log.Generations[g - 1].Best);
        }
    }

    [Fact]
    public void Bounds_clamp_and_settings_validation()
    {
        var b = new GeneBounds();
        Assert.Equal(new Params(64, 8.0, 0.25), b.Clamp(new Params(1000, 99, -1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GaSettings(Population: 1).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new GaSettings(Elite: 12).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new GaSettings(MutationRate: 2).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new GeneticAlgorithm(new GaSettings(TournamentSize: 0)).Run(Peak, 1));
    }

    [Fact]
    public void Evolution_log_csv_has_header_and_row_per_generation()
    {
        var (_, log) = new GeneticAlgorithm(new GaSettings(Population: 4, Generations: 2)).Run(Peak, 1);
        var csv = GeneticAlgorithm.ToCsv(log).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("generation,best,mean", csv[0]);
        Assert.Equal(4, csv.Length);
    }
}
