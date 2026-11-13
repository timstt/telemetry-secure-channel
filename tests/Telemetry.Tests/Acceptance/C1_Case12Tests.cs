using Telemetry.Tests.Infrastructure;

namespace Telemetry.Tests.Acceptance;

/// <summary>
/// C1 №12. Еталонні пакети приймаються; прогін 3 сценаріїв у ліміті часу.
/// Критерій: Усі ref_* Ok, усі bad_* — код з expected_failures.json; Runner 3 сценарії × 5 seed ≤ 120 с.
/// </summary>
public class C1_Case12Tests
{
    [Fact(Skip = TestData.Pending)]
    public void Case12() => Assert.Fail("Реалізувати після інтеграції модулів: Усі ref_* Ok, усі bad_* — код з expected_failures.json; Runner 3 сценарії × 5 seed ≤ 120 с.");
}
