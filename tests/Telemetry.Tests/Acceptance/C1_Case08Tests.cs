using Telemetry.Tests.Infrastructure;

namespace Telemetry.Tests.Acceptance;

/// <summary>
/// C1 №8. ДП-рюкзак не гірший за жадібний на всіх тестових наборах.
/// Критерій: Набори: вікна session.csv + випадкові; Σv(ДП) ≥ Σv(жадібний) і Σw(ДП) ≤ W.
/// </summary>
public class C1_Case08Tests
{
    [Fact(Skip = TestData.Pending)]
    public void Case08() => Assert.Fail("Реалізувати після інтеграції модулів: Набори: вікна session.csv + випадкові; Σv(ДП) ≥ Σv(жадібний) і Σw(ДП) ≤ W.");
}
