using Telemetry.Tests.Infrastructure;

namespace Telemetry.Tests.Acceptance;

/// <summary>
/// C1 №3. Спокійний сценарій: усі кадри відновлено точно.
/// Критерій: session.csv, сценарій «спокійний», 5 seed: множина відновлених кадрів == вхідна, побайтово.
/// </summary>
public class C1_Case03Tests
{
    [Fact(Skip = TestData.Pending)]
    public void Case03() => Assert.Fail("Реалізувати після інтеграції модулів: session.csv, сценарій «спокійний», 5 seed: множина відновлених кадрів == вхідна, побайтово.");
}
