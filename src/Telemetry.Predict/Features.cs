using System.Diagnostics.CodeAnalysis;
using Telemetry.Core;

namespace Telemetry.Predict;

/// <summary>
/// Ознаки з вікном історії: Δaltitude, Δtemp, |vibration|, ковзне відхилення вібрації й висоти,
/// частота зміни mode, critical. Нормалізація рахується на train і зберігається з вагами. Власник: Ваня.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Заглушка каркаса; прибрати атрибут разом з реалізацією")]
public static class Features
{
    /// <summary>Матриця ознак [кадр][ознака]; початок сеансу — без виходу за межі.</summary>
    public static double[][] Extract(Frame[] frames, int historyWindow) => throw new NotImplementedException();
}
