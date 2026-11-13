using Telemetry.Tests.Infrastructure;

namespace Telemetry.Tests.Acceptance;

/// <summary>
/// C1 №5. Аварійний сценарій: без аварійного завершення, Q обчислено.
/// Критерій: «аварійний», 5 seed: прогін завершується, лог без необроблених винятків, Q ∈ [0,1].
/// </summary>
public class C1_Case05Tests
{
    [Fact(Skip = TestData.Pending)]
    public void Case05() => Assert.Fail("Реалізувати після інтеграції модулів: «аварійний», 5 seed: прогін завершується, лог без необроблених винятків, Q ∈ [0,1].");
}
