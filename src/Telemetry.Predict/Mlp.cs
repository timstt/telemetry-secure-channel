using System.Diagnostics.CodeAnalysis;

namespace Telemetry.Predict;

/// <summary>
/// Багатошаровий перцептрон: Xavier, 3 виходи (softmax + крос-ентропія), SGD/Adam, зважена втрата
/// через дисбаланс класів. Лише власний код, без ML-бібліотек. Власник: Ваня.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Заглушка каркаса; прибрати атрибут разом з реалізацією")]
public sealed class Mlp
{
    /// <summary>Створює мережу із заданими розмірами шарів і seed ініціалізації.</summary>
    public Mlp(int[] layerSizes, int seed) => throw new NotImplementedException();

    /// <summary>Прямий прохід: імовірності класів.</summary>
    public double[] Forward(double[] x) => throw new NotImplementedException();

    /// <summary>Зберігає ваги й нормалізацію детерміновано.</summary>
    public void Save(string path) => throw new NotImplementedException();
}
