using System.Globalization;
using System.Text;
using Telemetry.Core;
using Telemetry.Predict;

namespace Telemetry.Tools;

/// <summary>
/// Офлайн-навчання класифікатора за протоколом team_config (80/20, ≥ 3 ініціалізації, вибір за валідацією),
/// одноразова оцінка на valid.csv, запис ваг у src/Telemetry.Predict/classifier.json (вбудовується в збірку)
/// і звіту з macro-F1 та матрицею помилок у out/classifier.md. Власник: Ваня.
/// </summary>
internal static class TrainClassifier
{
    public static int Run(string[] args)
    {
        var root = RepoPaths.Root();
        var seeds = args.Length > 0
            ? args[0].Split(',').Select(s => int.Parse(s, CultureInfo.InvariantCulture)).ToArray()
            : [1, 2, 3, 4, 5];
        var (trainFrames, trainLabels) = Load(Path.Combine(root, "data", "train.csv"));
        var (validFrames, validLabels) = Load(Path.Combine(root, "data", "valid.csv"));
        var config = TeamConfig.Load(Path.Combine(root, "data", "team_config.json"));

        var result = Trainer.Train(trainFrames, trainLabels, seeds, new TrainOptions());
        var predicted = new Classifier(result.Model).Predict(validFrames);
        var f1 = Metrics.MacroF1(validLabels, predicted, 3);
        var confusion = Metrics.Confusion(validLabels, predicted, 3);

        var report = new StringBuilder();
        report.AppendLine("# Класифікатор кадрів: результати навчання");
        report.AppendLine();
        report.AppendLine(CultureInfo.InvariantCulture, $"Архітектура: {string.Join(" → ", result.Model.LayerSizes)}, історія {result.Model.HistoryWindow} кадрів, Adam, зважена крос-ентропія.");
        report.AppendLine(CultureInfo.InvariantCulture, $"Розбиття train.csv: {Trainer.TrainShare:P0} навчання / решта валідація (хронологічно).");
        report.AppendLine();
        report.AppendLine("| Ініціалізація (seed) | macro-F1 на валідації | втрата |");
        report.AppendLine("|---:|---:|---:|");
        foreach (var init in result.Inits)
        {
            report.AppendLine(CultureInfo.InvariantCulture, $"| {init.Seed} | {init.ValidationMacroF1:F4} | {init.FinalLoss:F4} |");
        }

        report.AppendLine();
        report.AppendLine(CultureInfo.InvariantCulture, $"Обрана модель на valid.csv: **macro-F1 = {f1:F4}** (поріг F1_min = {config.Perceptron.F1Min}, мажоритарний клас = {config.Perceptron.MajorityBaseline}).");
        report.AppendLine();
        report.AppendLine("Матриця помилок на valid.csv (рядок — істинний клас, стовпець — передбачений):");
        report.AppendLine();
        report.AppendLine("| | 0 норма | 1 аномалія | 2 перехідний |");
        report.AppendLine("|---|---:|---:|---:|");
        string[] names = ["0 норма", "1 аномалія", "2 перехідний"];
        for (var t = 0; t < 3; t++)
        {
            report.AppendLine(CultureInfo.InvariantCulture, $"| {names[t]} | {confusion[t, 0]} | {confusion[t, 1]} | {confusion[t, 2]} |");
        }

        var text = report.ToString();
        Console.Write(text);
        result.Model.Save(Path.Combine(root, "src", "Telemetry.Predict", "classifier.json"));
        var outDir = Path.Combine(root, "out");
        Directory.CreateDirectory(outDir);
        File.WriteAllText(Path.Combine(outDir, "classifier.md"), text, new UTF8Encoding(false));
        return f1 >= config.Perceptron.F1Min ? 0 : 1;
    }

    private static (Frame[] Frames, int[] Labels) Load(string path)
    {
        var lines = File.ReadLines(path).Skip(1).Where(l => l.Length > 0).ToArray();
        return ([.. lines.Select(Frame.ParseCsv)], [.. lines.Select(Frame.ParseLabel)]);
    }
}

/// <summary>Корінь репозиторію (тека з Telemetry.sln) від поточної теки вгору.</summary>
internal static class RepoPaths
{
    public static string Root()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Telemetry.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new DirectoryNotFoundException("Не знайдено Telemetry.sln");
    }
}
