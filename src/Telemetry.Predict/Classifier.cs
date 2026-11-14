using Telemetry.Core;

namespace Telemetry.Predict;

/// <summary>Класифікатор кадрів 0/1/2 поверх <see cref="Mlp"/> і <see cref="Features"/>. Власник: Ваня.</summary>
/// <remarks>
/// Ваги навчено офлайн (<c>dotnet run --project tools/Telemetry.Tools -- train-classifier</c>) і вбудовано
/// в збірку як ресурс <see cref="WeightsResource"/>, тому навчання не входить у час прогону t і не залежить від шляхів.
/// </remarks>
public sealed class Classifier : IClassifier
{
    /// <summary>Ім'я вбудованого ресурсу з вагами.</summary>
    public const string WeightsResource = "Telemetry.Predict.classifier.json";

    private static readonly Lazy<Mlp> Embedded = new(LoadEmbedded);

    private readonly Mlp model;

    /// <summary>Класифікатор із вбудованими вагами.</summary>
    public Classifier()
        : this(Embedded.Value)
    {
    }

    /// <summary>Класифікатор із заданою мережею.</summary>
    public Classifier(Mlp model)
    {
        ArgumentNullException.ThrowIfNull(model);
        this.model = model;
    }

    /// <inheritdoc/>
    public int[] Predict(Frame[] frames)
    {
        ArgumentNullException.ThrowIfNull(frames);
        return [.. Features.Extract(frames, model.HistoryWindow).Select(model.Classify)];
    }

    /// <summary>Вбудовані ваги як JSON.</summary>
    /// <exception cref="InvalidOperationException">Ресурс відсутній у збірці.</exception>
    public static string EmbeddedWeightsJson()
    {
        using var stream = typeof(Classifier).Assembly.GetManifestResourceStream(WeightsResource)
            ?? throw new InvalidOperationException("Вбудовані ваги класифікатора відсутні.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static Mlp LoadEmbedded() => Mlp.FromJson(EmbeddedWeightsJson());
}

/// <summary>Класифікатор-заглушка: усім кадрам клас 0 (вимкнений блок «Прогноз» для таблиці 2).</summary>
public sealed class ConstantClassifier : IClassifier
{
    /// <inheritdoc/>
    public int[] Predict(Frame[] frames)
    {
        ArgumentNullException.ThrowIfNull(frames);
        return new int[frames.Length];
    }
}
