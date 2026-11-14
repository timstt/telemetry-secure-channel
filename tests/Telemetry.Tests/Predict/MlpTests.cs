using Telemetry.Core;
using Telemetry.Predict;
using Telemetry.Tests.Infrastructure;

namespace Telemetry.Tests.Predict;

/// <summary>Перцептрон, ознаки, протокол навчання і метрики (зона Вані).</summary>
public class MlpTests
{
    /// <summary>Три кластери в 2D — лінійно роздільні, навчання має досягти точності ≈ 1.</summary>
    private static (double[][] X, int[] Y) Clusters(int n, int seed)
    {
        var rng = new Random(seed);
        var x = new double[n][];
        var y = new int[n];
        double[][] centers = [[0, 0], [5, 5], [-5, 5]];
        for (var i = 0; i < n; i++)
        {
            y[i] = i % 3;
            x[i] = [centers[y[i]][0] + rng.NextDouble() - 0.5, centers[y[i]][1] + rng.NextDouble() - 0.5];
        }

        return (x, y);
    }

    [Fact]
    public void Learns_separable_clusters()
    {
        var (x, y) = Clusters(300, 1);
        var mlp = new Mlp([2, 8, 3], seed: 4);
        mlp.FitNormalization(x);
        var loss = mlp.Train(x, y, new TrainOptions(Epochs: 30, BatchSize: 16, LearningRate: 0.01));
        Assert.True(loss < 0.1);
        Assert.Equal(1.0, Metrics.MacroF1(y, x.Select(mlp.Classify).ToArray(), 3), 3);
        var p = mlp.Forward(x[0]);
        Assert.Equal(1.0, p.Sum(), 9);
    }

    [Fact]
    public void Save_and_load_give_identical_predictions()
    {
        var (x, y) = Clusters(60, 2);
        var mlp = new Mlp([2, 5, 4, 3], seed: 9) { HistoryWindow = 5 };
        mlp.FitNormalization(x);
        mlp.Train(x, y, new TrainOptions(Epochs: 3));
        var path = Path.Combine(Path.GetTempPath(), $"mlp_{Guid.NewGuid():N}.json");
        try
        {
            mlp.Save(path);
            var back = Mlp.FromJson(File.ReadAllText(path));
            Assert.Equal(5, back.HistoryWindow);
            Assert.Equal(mlp.LayerSizes, back.LayerSizes);
            Assert.All(x, row => Assert.Equal(mlp.Forward(row), back.Forward(row)));
            Assert.Equal(mlp.ToJson(), back.ToJson());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Same_seed_gives_same_network()
    {
        Assert.Equal(new Mlp([3, 4, 3], 7).ToJson(), new Mlp([3, 4, 3], 7).ToJson());
        Assert.NotEqual(new Mlp([3, 4, 3], 7).ToJson(), new Mlp([3, 4, 3], 8).ToJson());
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("not json")]
    [InlineData("{\"format\":\"telemetry-mlp-1\",\"history\":8,\"layers\":[2,3],\"mean\":[0],\"std\":[1,1],\"weights\":[],\"biases\":[]}")]
    [InlineData("{\"format\":\"telemetry-mlp-1\",\"history\":8,\"layers\":[2,3],\"mean\":[0,0],\"std\":[1,1],\"weights\":[],\"biases\":[]}")]
    [InlineData("{\"format\":\"telemetry-mlp-1\",\"history\":8,\"layers\":[2,3],\"mean\":[0,0],\"std\":[1,1],\"weights\":[[[1,1]]],\"biases\":[[0,0,0]]}")]
    public void Corrupted_weights_are_rejected(string json) =>
        Assert.Throws<InvalidDataException>(() => Mlp.FromJson(json));

    [Fact]
    public void Invalid_arguments_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => new Mlp([3], 1));
        Assert.Throws<ArgumentException>(() => new Mlp([3, 0, 3], 1));
        var mlp = new Mlp([2, 3], 1);
        Assert.Throws<ArgumentException>(() => mlp.Forward([1.0]));
        Assert.Throws<ArgumentException>(() => mlp.Train([[1, 2]], [5], new TrainOptions()));
        Assert.Throws<ArgumentException>(() => mlp.Train([[1, 2]], [], new TrainOptions()));
        mlp.FitNormalization([]);
        Assert.Equal(3, mlp.Forward([0, 0]).Length);
    }

    [Fact]
    public void Features_use_only_history_and_handle_session_start()
    {
        Frame[] frames = [new(0, 100, 200, 5, 1, 0), new(50, 104, 199, -300, 2, 1), new(100, 110, 205, 10, 2, 0)];
        var x = Features.Extract(frames, 8);
        Assert.Equal(3, x.Length);
        Assert.All(x, row => Assert.Equal(Features.Count, row.Length));
        Assert.Equal(0, x[0][2]);      // Δaltitude на першому кадрі
        Assert.Equal(4, x[1][2]);
        Assert.Equal(300, x[1][1]);    // |vibration|
        Assert.Equal(1.0, x[1][6]);    // частота зміни mode у вікні з 2 кадрів
        Assert.Equal(10, x[2][7]);     // дрейф висоти від початку вікна

        // Майбутні кадри не впливають на ознаки поточного.
        var prefix = Features.Extract(frames[..2], 8);
        Assert.Equal(prefix[1], x[1]);
        Assert.Throws<ArgumentOutOfRangeException>(() => Features.Extract(frames, 1));
        Assert.Empty(Features.Extract([], 8));
    }

    [Fact]
    public void Trainer_follows_protocol_and_picks_best_validation_init()
    {
        var lines = File.ReadLines(TestData.Path("data", "train.csv")).Skip(1).Take(600).ToArray();
        var frames = lines.Select(Frame.ParseCsv).ToArray();
        var labels = lines.Select(Frame.ParseLabel).ToArray();
        var result = Trainer.Train(frames, labels, [1, 2, 3], new TrainOptions(Epochs: 2), [Features.Count, 6, 3]);
        Assert.Equal(3, result.Inits.Count);
        Assert.All(result.Inits, i => Assert.InRange(i.ValidationMacroF1, 0, 1));
        Assert.Throws<ArgumentException>(() => Trainer.Train(frames, labels, [], new TrainOptions()));
        Assert.Throws<ArgumentException>(() => Trainer.Train(frames, labels[..5], [1], new TrainOptions()));
    }

    [Fact]
    public void Metrics_macro_f1_and_confusion()
    {
        int[] truth = [0, 0, 1, 1, 2, 2];
        int[] pred = [0, 1, 1, 1, 2, 0];
        var m = Metrics.Confusion(truth, pred, 3);
        Assert.Equal(1, m[0, 1]);
        Assert.Equal(1, m[2, 0]);
        Assert.Equal(1.0, Metrics.MacroF1(truth, truth, 3));
        Assert.Equal((0.5 + 0.8 + (2.0 / 3)) / 3, Metrics.MacroF1(truth, pred, 3), 9);
        Assert.Throws<ArgumentException>(() => Metrics.Confusion([0], [], 3));
    }

    [Fact]
    public void Constant_classifier_gives_class_zero_and_embedded_weights_load()
    {
        Assert.Equal(new int[4], new ConstantClassifier().Predict(new Frame[4]));
        Assert.Contains("telemetry-mlp-1", Classifier.EmbeddedWeightsJson(), StringComparison.Ordinal);
    }
}
