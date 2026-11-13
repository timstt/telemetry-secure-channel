using Telemetry.Core;
using Telemetry.System;
using Telemetry.Tests.Infrastructure;

namespace Telemetry.Tests.SystemZone;

/// <summary>Пастки 11, 14, 16, 18, 19 і планувальник — зона тімліда.</summary>
public class SystemTrapTests
{
    private static PacketPlan Plan(int index, int payloadBytes, int value) =>
        new(index, new byte[payloadBytes], [], value);

    [Fact(Skip = TestData.Pending)]
    public void Scheduler_orders_by_density_then_value_then_index()
    {
        var p = new[] { Plan(0, 100, 10), Plan(1, 100, 30), Plan(2, 10, 10), Plan(3, 100, 30) };
        var order = new Scheduler().Order(p, budgetSeconds: 100).Select(x => x.Index).ToArray();
        Assert.Equal(new[] { 2, 1, 3, 0 }, order);
    }

    [Fact(Skip = TestData.Pending)]
    public void Scheduler_is_deterministic()
    {
        var p = Enumerable.Range(0, 50).Select(i => Plan(i, 300 + (i % 7), i % 5)).ToArray();
        var s = new Scheduler();
        Assert.Equal(s.Order(p, 16.9).Select(x => x.Index), s.Order(p.Reverse().ToArray(), 16.9).Select(x => x.Index));
    }
}
