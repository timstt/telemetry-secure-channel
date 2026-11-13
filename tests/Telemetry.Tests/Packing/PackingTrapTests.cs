using Telemetry.Huffman;
using Telemetry.Packing;
using Telemetry.Tests.Infrastructure;

namespace Telemetry.Tests.Packing;

/// <summary>Пастка 10 і кейс C1 №8 (зона Георгія; жадібний — Саша).</summary>
public class PackingTrapTests
{
    [Fact(Skip = TestData.Pending)]
    public void Dp_is_optimal_where_greedy_is_not()
    {
        // Класичний контрприклад: v/w жадібно бере предмет 0, оптимум — 1 і 2.
        int[] w = [10, 20, 30];
        int[] v = [60, 100, 120];
        var dp = new KnapsackDp().Select(w, v, 50);
        var greedy = new KnapsackGreedy().Select(w, v, 50);
        Assert.Equal(220, dp.Sum(i => v[i]));
        Assert.True(dp.Sum(i => v[i]) >= greedy.Sum(i => v[i]));
        Assert.True(dp.Sum(i => w[i]) <= 50);
    }

    [Fact(Skip = TestData.Pending)]
    public void Edge_cases_empty_window_oversized_item_nonpositive_capacity()
    {
        var dp = new KnapsackDp();
        Assert.Empty(dp.Select([], [], 100));
        Assert.Empty(dp.Select([200], [5], 100));
        Assert.Empty(dp.Select([1], [5], 0));
        Assert.Empty(dp.Select([1], [5], -8));
    }
}
