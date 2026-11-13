using Telemetry.Tests.Infrastructure;

namespace Telemetry.Tests.Acceptance;

/// <summary>
/// C1 №6. Дерево Гаффмана відновлюється з канонічних довжин.
/// Критерій: Довжини ref_01..06 → канонічні коди за §3.3 == «код» у ref_*.json; декодування == кадри json.
/// </summary>
public class C1_Case06Tests
{
    [Fact(Skip = TestData.Pending)]
    public void Case06() => Assert.Fail("Реалізувати після інтеграції модулів: Довжини ref_01..06 → канонічні коди за §3.3 == «код» у ref_*.json; декодування == кадри json.");
}
