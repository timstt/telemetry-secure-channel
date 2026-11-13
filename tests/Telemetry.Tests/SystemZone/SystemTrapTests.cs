using Telemetry.Core;
using Telemetry.System;

namespace Telemetry.Tests.SystemZone;

/// <summary>Жадібний планувальник черги (зона тімліда).</summary>
public class SystemTrapTests
{
    private const double Bandwidth = 12000; // аварійний сценарій

    private static PacketPlan Plan(int index, int payloadBytes, int value) =>
        new(index, new byte[payloadBytes], [], value);

    /// <summary>Пакет із заданим розміром у бітах (повний, разом із 75 Б заголовка й HMAC).</summary>
    private static PacketPlan PlanBits(int index, long totalBits, int value) =>
        Plan(index, (int)(totalBits / 8) - PacketPlan.Overhead, value);

    [Fact]
    public void Scheduler_orders_by_density_then_value_then_index()
    {
        // b_k = 8·(75 + payload): 0 → 1400 біт (V 10), 1 і 3 → 1400 (V 30), 2 → 680 (V 10).
        // Щільності: 1 = 3 ≈ 0.0214 > 2 ≈ 0.0147 > 0 ≈ 0.0071; 1 і 3 рівні → менший Index.
        var p = new[] { Plan(0, 100, 10), Plan(1, 100, 30), Plan(2, 10, 10), Plan(3, 100, 30) };
        var order = new Scheduler().Order(p, budgetSeconds: 100).Select(x => x.Index).ToArray();
        Assert.Equal(new[] { 1, 3, 2, 0 }, order);
    }

    [Fact]
    public void Equal_density_prefers_larger_value()
    {
        // 800 біт / V 4 і 1600 біт / V 8 — однакова щільність, раніше той, у кого більша V.
        var p = new[] { PlanBits(0, 800, 4), PlanBits(1, 1600, 8) };
        Assert.Equal(new[] { 1, 0 }, new Scheduler().Order(p, 10).Select(x => x.Index));
    }

    [Fact]
    public void Scheduler_is_deterministic_and_input_order_independent()
    {
        var p = Enumerable.Range(0, 50).Select(i => Plan(i, 300 + (i % 7), i % 5)).ToArray();
        var s = new Scheduler();
        var forward = s.Order(p, 16.9).Select(x => x.Index).ToArray();
        Assert.Equal(forward, s.Order(p.Reverse().ToArray(), 16.9).Select(x => x.Index));
        Assert.Equal(forward, s.Order(p, 16.9).Select(x => x.Index));
    }

    [Fact]
    public void Scheduler_does_not_mutate_input_and_keeps_all_packets()
    {
        var p = new[] { Plan(0, 10, 1), Plan(1, 10, 9) };
        var result = new Scheduler().Order(p, 1);
        Assert.Equal(0, p[0].Index);
        Assert.Equal(2, result.Length);
        Assert.Empty(new Scheduler().Order([], 1));
        Assert.Throws<ArgumentNullException>(() => new Scheduler().Order(null!, 1));
    }

    [Fact]
    public void Greedy_counterexample_on_three_packets()
    {
        // Бюджет 50 одиниць часу; (V, t): A (60, 10), B (100, 20), C (120, 30).
        // Щільність: A 6 > B 5 > C 4 → канал бере A, B (30), C не влазить → 160. Оптимум B + C = 220.
        var unit = (long)Bandwidth; // 1 одиниця часу = 1 с = 12000 біт
        var a = PlanBits(0, 10 * unit, 60);
        var b = PlanBits(1, 20 * unit, 100);
        var c = PlanBits(2, 30 * unit, 120);
        var order = new Scheduler().Order([c, b, a], 50);
        Assert.Equal(new[] { 0, 1, 2 }, order.Select(x => x.Index));
        Assert.Equal(160, DeliveredValue(order, 50));
        Assert.Equal(220, b.TotalValue + c.TotalValue);
    }

    [Fact]
    public void Best_of_two_beats_greedy_when_one_big_packet_dominates()
    {
        // Маленький щільний пакет з'їдає бюджет, а великий цінний уже не влазить.
        var unit = (long)Bandwidth;
        var small = PlanBits(0, 1 * unit, 2);   // щільність 2
        var big = PlanBits(1, 10 * unit, 10);   // щільність 1, але V = 10
        var greedy = DeliveredValue(new Scheduler().Order([small, big], 10), 10);
        var best = Scheduler.BestOfTwo([small, big], 10, Bandwidth);
        Assert.Equal(2, greedy);
        Assert.Equal(new[] { 1 }, best.Select(x => x.Index));
    }

    [Fact]
    public void Best_of_two_is_at_least_half_of_optimum_on_random_instances()
    {
        var rng = new Random(3);
        for (var trial = 0; trial < 300; trial++)
        {
            var n = rng.Next(1, 9);
            var p = Enumerable.Range(0, n)
                .Select(i => PlanBits(i, 8L * rng.Next(80, 2000), rng.Next(1, 40)))
                .ToArray();
            var budget = rng.Next(1, 3);
            var best = Scheduler.BestOfTwo(p, budget, Bandwidth);
            Assert.True(TotalTime(best) <= budget + 1e-9);
            Assert.True(2 * best.Sum(x => x.TotalValue) >= BruteForceOptimum(p, budget));
        }
    }

    [Fact]
    public void Near_equal_sizes_reduce_to_sorting_by_value()
    {
        var p = Enumerable.Range(0, 20).Select(i => Plan(i, 430 + (i % 2), (i * 7) % 11)).ToArray();
        var byScheduler = new Scheduler().Order(p, 1).Select(x => x.TotalValue).ToArray();
        Assert.Equal(byScheduler.OrderByDescending(v => v), byScheduler);
    }

    [Fact]
    public void Fifo_keeps_creation_order()
    {
        var p = new[] { Plan(2, 10, 1), Plan(0, 10, 9), Plan(1, 10, 5) };
        Assert.Equal(new[] { 0, 1, 2 }, new FifoScheduler().Order(p, 1).Select(x => x.Index));
    }

    [Fact]
    public void Random_scheduler_is_a_reproducible_permutation()
    {
        var p = Enumerable.Range(0, 30).Select(i => Plan(i, 10, i)).ToArray();
        var a = new RandomScheduler(5).Order(p, 1).Select(x => x.Index).ToArray();
        Assert.Equal(a, new RandomScheduler(5).Order(p, 1).Select(x => x.Index));
        Assert.Equal(Enumerable.Range(0, 30), a.Order());
        Assert.NotEqual(Enumerable.Range(0, 30), a);
    }

    [Fact]
    public void Scheduler_beats_fifo_when_budget_is_short()
    {
        // Пізні пакети цінніші (як критичні кадри наприкінці черги) — FIFO витрачає бюджет на дешеві.
        var p = Enumerable.Range(0, 40).Select(i => Plan(i, 400, i < 30 ? 1 : 10)).ToArray();
        const double budget = 4.0; // ≈ 10 пакетів по 3800 біт при 12 кбіт/с
        Assert.True(DeliveredValue(new Scheduler().Order(p, budget), budget)
                    > DeliveredValue(new FifoScheduler().Order(p, budget), budget));
    }

    /// <summary>Модель бюджету channel.py: пакет, що не влазить, пропускається, наступні ще можуть пройти.</summary>
    private static long DeliveredValue(IEnumerable<PacketPlan> order, double budget)
    {
        double clock = 0;
        long value = 0;
        foreach (var p in order)
        {
            var t = p.SizeBits / Bandwidth;
            if (clock + t > budget)
            {
                continue;
            }

            clock += t;
            value += p.TotalValue;
        }

        return value;
    }

    private static double TotalTime(IEnumerable<PacketPlan> set) => set.Sum(p => p.SizeBits / Bandwidth);

    private static long BruteForceOptimum(PacketPlan[] p, double budget)
    {
        long best = 0;
        for (var mask = 0; mask < (1 << p.Length); mask++)
        {
            var chosen = p.Where((_, i) => (mask & (1 << i)) != 0).ToArray();
            if (TotalTime(chosen) <= budget)
            {
                best = Math.Max(best, chosen.Sum(x => (long)x.TotalValue));
            }
        }

        return best;
    }
}
