using System.Diagnostics.CodeAnalysis;
using Telemetry.Core;

namespace Telemetry.System;

/// <summary>
/// Жадібний планувальник черги: сортування за щільністю V_k / t_k за спаданням
/// (рівні — більший V_k, далі менший Index), O(n log n). Детермінований, не залежить від seed каналу.
/// Вимкнений планувальник = FIFO. Власник: тімлід.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Заглушка каркаса; прибрати атрибут разом з реалізацією")]
public sealed class Scheduler : IScheduler
{
    /// <inheritdoc/>
    public PacketPlan[] Order(PacketPlan[] packets, double budgetSeconds) => throw new NotImplementedException();
}
