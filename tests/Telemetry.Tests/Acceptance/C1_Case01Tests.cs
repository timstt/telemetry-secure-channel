using Telemetry.Tests.Infrastructure;

namespace Telemetry.Tests.Acceptance;

/// <summary>
/// C1 №1. Потік із одного кадру проходить і відновлюється побайтово.
/// Критерій: Sender → канал «спокійний» → Receiver на 1 кадрі; відновлені 12 байтів == вхідні; ref_01 читається.
/// </summary>
public class C1_Case01Tests
{
    [Fact(Skip = TestData.Pending)]
    public void Case01() => Assert.Fail("Реалізувати після інтеграції модулів: Sender → канал «спокійний» → Receiver на 1 кадрі; відновлені 12 байтів == вхідні; ref_01 читається.");
}
