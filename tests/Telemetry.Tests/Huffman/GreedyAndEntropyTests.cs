using Telemetry.Huffman;

namespace Telemetry.Tests.Huffman;

public class GreedyAndEntropyTests
{
    [Fact]
    public void Greedy_is_not_optimal_on_classic_counterexample()
    {
        int[] w = [10, 20, 30];
        int[] v = [60, 100, 120];
        var chosen = new KnapsackGreedy().Select(w, v, 50);
        Assert.Equal(new[] { 0, 1 }, chosen);
        Assert.Equal(160, chosen.Sum(i => v[i]));
    }

    [Fact]
    public void Greedy_takes_best_density_first_and_skips_items_that_do_not_fit()
    {
        int[] w = [5, 4, 3, 100];
        int[] v = [10, 4, 9, 1000];
        var chosen = new KnapsackGreedy().Select(w, v, 8);
        Assert.Equal(new[] { 0, 2 }, chosen);
    }

    [Fact]
    public void Greedy_breaks_density_ties_by_value_then_index()
    {
        int[] w = [2, 4, 2];
        int[] v = [2, 4, 2];
        Assert.Equal(new[] { 1 }, new KnapsackGreedy().Select(w, v, 4));
        Assert.Equal(new[] { 0, 1 }, new KnapsackGreedy().Select([2, 2, 2], [2, 2, 2], 4));
    }

    [Fact]
    public void Greedy_edge_cases()
    {
        var greedy = new KnapsackGreedy();
        Assert.Empty(greedy.Select([], [], 100));
        Assert.Empty(greedy.Select([200], [5], 100));
        Assert.Empty(greedy.Select([1], [5], 0));
        Assert.Empty(greedy.Select([1], [5], -8));
        Assert.Throws<ArgumentException>(() => greedy.Select([1, 2], [1], 10));
    }

    [Fact]
    public void Greedy_result_always_fits_capacity()
    {
        var rnd = new Random(3);
        var greedy = new KnapsackGreedy();
        for (var round = 0; round < 200; round++)
        {
            var n = rnd.Next(0, 40);
            var w = Enumerable.Range(0, n).Select(_ => rnd.Next(1, 200)).ToArray();
            var v = Enumerable.Range(0, n).Select(_ => rnd.Next(1, 11)).ToArray();
            var capacity = rnd.Next(1, 1500);
            var chosen = greedy.Select(w, v, capacity);
            Assert.True(chosen.Sum(i => w[i]) <= capacity);
            Assert.Equal(chosen.OrderBy(i => i), chosen);
        }
    }

    [Fact]
    public void Entropy_of_constant_and_empty_data_is_zero()
    {
        Assert.Equal(0.0, NoCompression.Entropy([]));
        Assert.Equal(0.0, NoCompression.Entropy([5, 5, 5, 5]), 9);
        Assert.False(NoCompression.ShouldSkipCompression([], 7.5));
        Assert.False(NoCompression.ShouldSkipCompression([5, 5, 5, 5], 7.5));
    }

    [Fact]
    public void Entropy_of_two_equiprobable_symbols_is_one_bit()
    {
        Assert.Equal(1.0, NoCompression.Entropy([0, 1, 0, 1]), 9);
    }
}
