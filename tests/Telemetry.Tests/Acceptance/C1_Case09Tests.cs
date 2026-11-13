using Telemetry.Tests.Infrastructure;

namespace Telemetry.Tests.Acceptance;

/// <summary>
/// C1 №9. Класифікатор призначає клас і пріоритет усім кадрам.
/// Критерій: Predict(session) має довжину 3000, класи 0..2, пріоритет 1/3/5; macro-F1 на валідації ≥ 0.58.
/// </summary>
public class C1_Case09Tests
{
    [Fact(Skip = TestData.Pending)]
    public void Case09() => Assert.Fail("Реалізувати після інтеграції модулів: Predict(session) має довжину 3000, класи 0..2, пріоритет 1/3/5; macro-F1 на валідації ≥ 0.58.");
}
