using Telemetry.Tests.Infrastructure;

namespace Telemetry.Tests.Acceptance;

/// <summary>
/// C1 №4. Робочий сценарій: частка втрачених критичних ≤ порогу викладача.
/// Критерій: «робочий», 5 seed: 1 − R ≤ 0.05 на КОЖНОМУ seed (запас малий, пастка 21).
/// </summary>
public class C1_Case04Tests
{
    [Fact(Skip = TestData.Pending)]
    public void Case04() => Assert.Fail("Реалізувати після інтеграції модулів: «робочий», 5 seed: 1 − R ≤ 0.05 на КОЖНОМУ seed (запас малий, пастка 21).");
}
