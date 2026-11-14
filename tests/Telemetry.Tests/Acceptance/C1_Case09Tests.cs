using Telemetry.Core;
using Telemetry.Predict;
using Telemetry.Tests.Infrastructure;

namespace Telemetry.Tests.Acceptance;

/// <summary>
/// C1 №9. Класифікатор призначає клас і пріоритет усім кадрам.
/// Критерій: Predict(session) має довжину 3000, класи 0..2, пріоритет 1/3/5; macro-F1 на валідації ≥ 0.58.
/// </summary>
public class C1_Case09Tests
{
    private static (Frame[] Frames, int[] Labels) Load(string name)
    {
        var lines = File.ReadLines(TestData.Path("data", name)).Skip(1).Where(l => l.Length > 0).ToArray();
        return ([.. lines.Select(Frame.ParseCsv)], [.. lines.Select(Frame.ParseLabel)]);
    }

    [Fact]
    public void Case09_every_session_frame_gets_class_and_priority()
    {
        var config = TeamConfig.Load(TestData.Path("data", "team_config.json"));
        var (frames, _) = Load("session.csv");
        var classes = new Classifier().Predict(frames);
        Assert.Equal(3000, classes.Length);
        Assert.All(classes, c => Assert.InRange(c, 0, 2));
        Assert.All(classes, c => Assert.Contains(config.Priorities.ForClass(c), new[] { 1, 3, 5 }));
        Assert.Equal(3, classes.Distinct().Count());
    }

    [Fact]
    public void Case09_macro_f1_on_valid_is_above_threshold_with_margin()
    {
        var config = TeamConfig.Load(TestData.Path("data", "team_config.json"));
        var (frames, labels) = Load("valid.csv");
        var f1 = Metrics.MacroF1(labels, new Classifier().Predict(frames), 3);
        Assert.True(f1 >= config.Perceptron.F1Min + 0.15, $"macro-F1 = {f1:F4}");
    }
}
