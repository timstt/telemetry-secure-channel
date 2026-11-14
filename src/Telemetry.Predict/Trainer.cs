using Telemetry.Core;

namespace Telemetry.Predict;

/// <summary>Результат однієї ініціалізації.</summary>
/// <param name="Seed">Seed ініціалізації ваг.</param>
/// <param name="ValidationMacroF1">macro-F1 на валідаційній частині train (20 %).</param>
/// <param name="FinalLoss">Втрата останньої епохи.</param>
public sealed record InitResult(int Seed, double ValidationMacroF1, double FinalLoss);

/// <summary>Підсумок навчання: обрана мережа і результати всіх ініціалізацій.</summary>
/// <param name="Model">Мережа з найкращим macro-F1 на валідаційній частині.</param>
/// <param name="Inits">Усі ініціалізації.</param>
public sealed record TrainResult(Mlp Model, IReadOnlyList<InitResult> Inits);

/// <summary>
/// Протокол навчання з team_config.перцептрон.протокол_навчання: TRAIN ділиться 80/20 (хронологічно —
/// ознаки беруть історію, тож перемішування до розбиття дало б витік), ≥ 3 ініціалізації, вибір моделі
/// за macro-F1 на валідаційній частині. Оцінка на окремому наборі (valid.csv) — один раз, поза цим класом.
/// Власник: Ваня.
/// </summary>
public static class Trainer
{
    /// <summary>Частка навчальної частини.</summary>
    public const double TrainShare = 0.8;

    /// <summary>Архітектура за замовчуванням: ознаки → 32 → 16 → 3.</summary>
    public static int[] DefaultLayers => [Features.Count, 32, 16, 3];

    /// <summary>Навчає <paramref name="seeds"/>.Length ініціалізацій і повертає найкращу за валідацією.</summary>
    /// <exception cref="ArgumentException">Немає seed або розміри не збігаються.</exception>
    public static TrainResult Train(
        Frame[] frames, int[] labels, IReadOnlyList<int> seeds, TrainOptions options, int[]? layers = null,
        int historyWindow = Features.DefaultHistory)
    {
        ArgumentNullException.ThrowIfNull(frames);
        ArgumentNullException.ThrowIfNull(labels);
        ArgumentNullException.ThrowIfNull(seeds);
        ArgumentNullException.ThrowIfNull(options);
        if (seeds.Count == 0 || frames.Length != labels.Length || frames.Length < 10)
        {
            throw new ArgumentException("Потрібен хоча б один seed і щонайменше 10 розмічених кадрів.");
        }

        layers ??= DefaultLayers;
        var x = Features.Extract(frames, historyWindow);
        var cut = (int)(frames.Length * TrainShare);
        var xTrain = x[..cut];
        var yTrain = labels[..cut];
        var xVal = x[cut..];
        var yVal = labels[cut..];

        // Ваги класів обернено пропорційні частоті (дисбаланс 6243/801/956), нормовані до середнього 1.
        var classes = layers[^1];
        var counts = new double[classes];
        foreach (var c in yTrain)
        {
            counts[c]++;
        }

        var weights = counts.Select(c => c > 0 ? yTrain.Length / (classes * c) : 0).ToArray();
        var opts = options with { ClassWeights = options.ClassWeights ?? weights };

        Mlp? best = null;
        var bestF1 = double.NegativeInfinity;
        var inits = new List<InitResult>();
        foreach (var seed in seeds)
        {
            var mlp = new Mlp(layers, seed) { HistoryWindow = historyWindow };
            mlp.FitNormalization(xTrain);
            var loss = mlp.Train(xTrain, yTrain, opts with { Seed = seed });
            var f1 = Metrics.MacroF1(yVal, xVal.Select(mlp.Classify).ToArray(), classes);
            inits.Add(new InitResult(seed, f1, loss));
            if (f1 > bestF1)
            {
                bestF1 = f1;
                best = mlp;
            }
        }

        return new TrainResult(best!, inits);
    }
}

/// <summary>Метрики класифікації.</summary>
public static class Metrics
{
    /// <summary>Матриця помилок [істинний][передбачений].</summary>
    public static int[,] Confusion(int[] truth, int[] predicted, int classes)
    {
        ArgumentNullException.ThrowIfNull(truth);
        ArgumentNullException.ThrowIfNull(predicted);
        if (truth.Length != predicted.Length)
        {
            throw new ArgumentException("Різна довжина міток і прогнозів.");
        }

        var m = new int[classes, classes];
        for (var i = 0; i < truth.Length; i++)
        {
            m[truth[i], predicted[i]]++;
        }

        return m;
    }

    /// <summary>macro-F1: середнє F1 за класами (клас без прикладів і прогнозів дає F1 = 0).</summary>
    public static double MacroF1(int[] truth, int[] predicted, int classes)
    {
        var m = Confusion(truth, predicted, classes);
        var sum = 0.0;
        for (var k = 0; k < classes; k++)
        {
            double tp = m[k, k], fp = 0, fn = 0;
            for (var j = 0; j < classes; j++)
            {
                if (j != k)
                {
                    fp += m[j, k];
                    fn += m[k, j];
                }
            }

            sum += tp == 0 ? 0 : 2 * tp / ((2 * tp) + fp + fn);
        }

        return sum / classes;
    }
}
