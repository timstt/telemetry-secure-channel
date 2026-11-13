using Telemetry.Tests.Infrastructure;

namespace Telemetry.Tests.Acceptance;

/// <summary>
/// C1 №7. Вироджений алфавіт кодується коректно.
/// Критерій: Один символ → length=1, код «0»; ref_03 читається; encode→decode тотожність.
/// </summary>
public class C1_Case07Tests
{
    [Fact(Skip = TestData.Pending)]
    public void Case07() => Assert.Fail("Реалізувати після інтеграції модулів: Один символ → length=1, код «0»; ref_03 читається; encode→decode тотожність.");
}
