namespace Telemetry.System;

/// <summary>Складові і значення метрики Q.</summary>
/// <param name="S">Ефективність передавання B_ref/(B_ref+B_sent).</param>
/// <param name="R">Частка відновлених критичних кадрів.</param>
/// <param name="T">Швидкодія t_ref/(t_ref+t).</param>
/// <param name="Q">α·S + β·R + γ·T, або 0, якщо приймач упав чи R = 0.</param>
public sealed record QResult(double S, double R, double T, double Q);

/// <summary>
/// Метрика Q (PROJECT_1 §4, team_config.метрика_Q). Власник: тімлід.
/// </summary>
/// <remarks>
/// Припущення про одиниці (README): B_ref = 8·12·кадрів, B_sent — усі біти, віддані в канал, включно з
/// утраченими пакетами; невміщені в бюджет пакети в B_sent не входять. t — час обчислень системи, без
/// часу каналу. Якщо приймач аварійно завершився або R = 0, Q = 0 (складові все одно повертаються для звіту).
/// </remarks>
public static class Metric
{
    /// <summary>Обчислює Q.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Від'ємні обсяги чи час, R поза [0,1], t_ref ≤ 0.</exception>
    public static QResult Compute(
        long bRefBits, long bSentBits, double recoveredCriticalShare, double runSeconds,
        double alpha, double beta, double gamma, double tRefSeconds, bool receiverCrashed)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bRefBits);
        ArgumentOutOfRangeException.ThrowIfNegative(bSentBits);
        ArgumentOutOfRangeException.ThrowIfNegative(runSeconds);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tRefSeconds);
        if (double.IsNaN(recoveredCriticalShare) || recoveredCriticalShare is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(recoveredCriticalShare), recoveredCriticalShare, "R має бути в [0, 1]");
        }

        // Нічого не передано: S = 1 (жодного зайвого біта), а не 0/0.
        var s = bRefBits + bSentBits == 0 ? 1.0 : (double)bRefBits / (bRefBits + bSentBits);
        var r = recoveredCriticalShare;
        var t = tRefSeconds / (tRefSeconds + runSeconds);
        var q = receiverCrashed || r == 0 ? 0.0 : (alpha * s) + (beta * r) + (gamma * t);
        return new QResult(s, r, t, q);
    }

    /// <summary>Обчислює Q з коефіцієнтами з team_config.json.</summary>
    public static QResult Compute(
        Telemetry.Core.MetricConfig cfg, long bRefBits, long bSentBits, double recoveredCriticalShare,
        double runSeconds, bool receiverCrashed)
    {
        ArgumentNullException.ThrowIfNull(cfg);
        return Compute(bRefBits, bSentBits, recoveredCriticalShare, runSeconds,
            cfg.Alpha, cfg.Beta, cfg.Gamma, cfg.TRefSeconds, receiverCrashed);
    }
}
