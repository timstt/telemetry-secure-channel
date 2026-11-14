using Telemetry.Core;
using Telemetry.Huffman;
using Telemetry.Packing;
using Telemetry.Tests.Infrastructure;

namespace Telemetry.Tests.Acceptance;

/// <summary>
/// C1 №8. ДП-рюкзак не гірший за жадібний на всіх тестових наборах.
/// Критерій: Набори: вікна session.csv + випадкові; Σv(ДП) ≥ Σv(жадібний) і Σw(ДП) ≤ W.
/// </summary>
public class C1_Case08Tests
{
    [Fact]
    public void Case08_session_windows()
    {
        var config = TeamConfig.Load(TestData.Path("data", "team_config.json"));
        var lines = File.ReadLines(TestData.Path("data", "session.csv")).Skip(1).Where(l => l.Length > 0).ToArray();
        var frames = lines.Select(Frame.ParseCsv).ToArray();
        var labels = lines.Select(Frame.ParseLabel).ToArray();
        var codec = new HuffmanCodec();
        var window = config.Packing.CandidateWindowFrames;
        var checkedWindows = 0;
        foreach (var size in new[] { 16, 32, window })
        {
            for (var start = 0; start + size <= frames.Length; start += size)
            {
                var win = frames[start..(start + size)];
                var freq = new long[256];
                foreach (var b in Frame.Serialize(win))
                {
                    freq[b]++;
                }

                var lengths = codec.BuildLengths(freq);
                var w = win.Select(f => f.ToBytes().Sum(b => lengths[b])).ToArray();
                var v = win.Select((f, i) => config.Priorities.Value(labels[start + i], f.IsCritical)).ToArray();
                var capacity = config.Packing.CapacityBits(lengths.Count(l => l != 0));
                AssertDpNotWorse(w, v, capacity);
                checkedWindows++;
            }
        }

        Assert.True(checkedWindows > 100);
    }

    [Fact]
    public void Case08_random_sets_and_brute_force()
    {
        var rng = new Random(2026);
        for (var trial = 0; trial < 1000; trial++)
        {
            var n = rng.Next(0, 13);
            var w = Enumerable.Range(0, n).Select(_ => rng.Next(1, 200)).ToArray();
            var v = Enumerable.Range(0, n).Select(_ => rng.Next(1, 12)).ToArray();
            var capacity = rng.Next(-10, 800);
            var dp = AssertDpNotWorse(w, v, capacity);
            Assert.Equal(BruteForce(w, v, capacity), KnapsackDp.TotalValue(dp, v));
        }
    }

    private static int[] AssertDpNotWorse(int[] w, int[] v, int capacity)
    {
        var dp = new KnapsackDp().Select(w, v, capacity);
        var greedy = new KnapsackGreedy().Select(w, v, capacity);
        Assert.True(KnapsackDp.TotalValue(dp, v) >= KnapsackDp.TotalValue(greedy, v));
        Assert.True(KnapsackDp.TotalWeight(dp, w) <= Math.Max(0, capacity));
        Assert.Equal(dp.Distinct().Order(), dp);
        return dp;
    }

    private static long BruteForce(int[] w, int[] v, int capacity)
    {
        long best = 0;
        for (var mask = 0; mask < 1 << w.Length; mask++)
        {
            long weight = 0, value = 0;
            for (var i = 0; i < w.Length; i++)
            {
                if ((mask & (1 << i)) != 0)
                {
                    weight += w[i];
                    value += v[i];
                }
            }

            if (weight <= capacity)
            {
                best = Math.Max(best, value);
            }
        }

        return best;
    }
}
