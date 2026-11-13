using System.Diagnostics.CodeAnalysis;
using Telemetry.Core;

namespace Telemetry.Predict;

/// <summary>Класифікатор кадрів 0/1/2 поверх <see cref="Mlp"/> і <see cref="Features"/>. Власник: Ваня.</summary>
[ExcludeFromCodeCoverage(Justification = "Заглушка каркаса; прибрати атрибут разом з реалізацією")]
public sealed class Classifier : IClassifier
{
    /// <inheritdoc/>
    public int[] Predict(Frame[] frames) => throw new NotImplementedException();
}
