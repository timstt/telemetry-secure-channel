using Telemetry.Tests.Infrastructure;

namespace Telemetry.Tests.Acceptance;

/// <summary>
/// C1 №11. Невірний пароль → відмова; обрізаний пакет → відмова без падіння.
/// Критерій: Невірний пароль → E_HMAC, payload == null; bad_03 → E_LENGTH; усі префікси пакета → відмова без винятку.
/// </summary>
public class C1_Case11Tests
{
    [Fact(Skip = TestData.Pending)]
    public void Case11() => Assert.Fail("Реалізувати після інтеграції модулів: Невірний пароль → E_HMAC, payload == null; bad_03 → E_LENGTH; усі префікси пакета → відмова без винятку.");
}
