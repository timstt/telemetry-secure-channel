using Telemetry.Tests.Infrastructure;

namespace Telemetry.Tests.Acceptance;

/// <summary>
/// C1 №10. Пакет зі зміненим байтом і зі зміненим HMAC відхиляються.
/// Критерій: bad_01 → E_HMAC, bad_02 → E_HMAC; власний пакет з інвертованим байтом у кожній позиції → відмова.
/// </summary>
public class C1_Case10Tests
{
    [Fact(Skip = TestData.Pending)]
    public void Case10() => Assert.Fail("Реалізувати після інтеграції модулів: bad_01 → E_HMAC, bad_02 → E_HMAC; власний пакет з інвертованим байтом у кожній позиції → відмова.");
}
