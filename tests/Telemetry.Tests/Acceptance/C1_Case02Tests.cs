using Telemetry.Tests.Infrastructure;

namespace Telemetry.Tests.Acceptance;

/// <summary>
/// C1 №2. Порожній потік не спричиняє аварійного завершення.
/// Критерій: 0 кадрів: без винятків, 0 відновлених кадрів, Q обчислено (R=0 → Q=0 за правилом метрики).
/// </summary>
public class C1_Case02Tests
{
    [Fact(Skip = TestData.Pending)]
    public void Case02() => Assert.Fail("Реалізувати після інтеграції модулів: 0 кадрів: без винятків, 0 відновлених кадрів, Q обчислено (R=0 → Q=0 за правилом метрики).");
}
