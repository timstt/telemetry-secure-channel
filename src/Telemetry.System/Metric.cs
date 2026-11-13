using System.Diagnostics.CodeAnalysis;

namespace Telemetry.System;

/// <summary>Складові і значення метрики Q.</summary>
public sealed record QResult(double S, double R, double T, double Q);

/// <summary>
/// Q = α·S + β·R + γ·T; S = B_ref/(B_ref+B_sent), R — частка відновлених критичних, T = t_ref/(t_ref+t).
/// B_ref і B_sent — у бітах; B_sent включає втрачені пакети, не включає невміщені в бюджет.
/// Час каналу в t не входить. Приймач упав або R = 0 → Q = 0. Власник: тімлід.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Заглушка каркаса; прибрати атрибут разом з реалізацією")]
public static class Metric
{
    /// <summary>Обчислює Q.</summary>
    public static QResult Compute(
        long bRefBits, long bSentBits, double recoveredCriticalShare, double runSeconds,
        double alpha, double beta, double gamma, double tRefSeconds, bool receiverCrashed) =>
        throw new NotImplementedException();
}
