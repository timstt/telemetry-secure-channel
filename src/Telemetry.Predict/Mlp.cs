using System.Text.Json;
using System.Text.Json.Nodes;

namespace Telemetry.Predict;

/// <summary>Параметри навчання перцептрона.</summary>
/// <param name="Epochs">Кількість епох.</param>
/// <param name="BatchSize">Розмір міні-батчу.</param>
/// <param name="LearningRate">Крок Adam.</param>
/// <param name="ClassWeights">Ваги класів у крос-ентропії (дисбаланс); null — усі 1.</param>
/// <param name="Seed">Seed перемішування батчів.</param>
public sealed record TrainOptions(int Epochs = 40, int BatchSize = 32, double LearningRate = 0.003, double[]? ClassWeights = null, int Seed = 1);

/// <summary>
/// Багатошаровий перцептрон: Xavier, 3 виходи (softmax + крос-ентропія), SGD/Adam, зважена втрата
/// через дисбаланс класів. Лише власний код, без ML-бібліотек. Власник: Ваня.
/// </summary>
/// <remarks>
/// Приховані шари — ReLU, вихід — softmax. Вхід нормалізується z-оцінкою (середнє і σ з train), нормалізація
/// зберігається разом із вагами, тому приймач/передавач отримує той самий прогноз без даних навчання.
/// Навчання — міні-батч Adam (β1 = 0,9, β2 = 0,999), градієнт крос-ентропії по логітах = p − onehot, зважений вагою класу.
/// </remarks>
public sealed class Mlp
{
    private readonly int[] sizes;
    private readonly double[][][] weights; // [шар][вихід][вхід]
    private readonly double[][] biases;    // [шар][вихід]
    private double[] mean;
    private double[] std;

    /// <summary>Створює мережу із заданими розмірами шарів і seed ініціалізації.</summary>
    /// <exception cref="ArgumentException">Менше двох шарів або нульовий розмір.</exception>
    public Mlp(int[] layerSizes, int seed)
    {
        ArgumentNullException.ThrowIfNull(layerSizes);
        if (layerSizes.Length < 2 || layerSizes.Any(s => s <= 0))
        {
            throw new ArgumentException("Потрібно щонайменше два шари додатного розміру.", nameof(layerSizes));
        }

        sizes = (int[])layerSizes.Clone();
        weights = new double[sizes.Length - 1][][];
        biases = new double[sizes.Length - 1][];
#pragma warning disable CA5394 // не криптографічне використання: ініціалізація ваг
        var rng = new Random(seed);
        for (var l = 0; l < weights.Length; l++)
        {
            var fanIn = sizes[l];
            var fanOut = sizes[l + 1];
            var limit = Math.Sqrt(6.0 / (fanIn + fanOut)); // Xavier/Glorot uniform
            weights[l] = new double[fanOut][];
            biases[l] = new double[fanOut];
            for (var o = 0; o < fanOut; o++)
            {
                weights[l][o] = new double[fanIn];
                for (var i = 0; i < fanIn; i++)
                {
                    weights[l][o][i] = ((rng.NextDouble() * 2) - 1) * limit;
                }
            }
        }
#pragma warning restore CA5394

        mean = new double[sizes[0]];
        std = Enumerable.Repeat(1.0, sizes[0]).ToArray();
    }

    /// <summary>Розміри шарів.</summary>
    public IReadOnlyList<int> LayerSizes => sizes;

    /// <summary>Глибина історії ознак, з якою навчено мережу (зберігається з вагами).</summary>
    public int HistoryWindow { get; set; } = Features.DefaultHistory;

    /// <summary>Рахує нормалізацію (середнє, σ) за матрицею ознак train.</summary>
    public void FitNormalization(double[][] x)
    {
        ArgumentNullException.ThrowIfNull(x);
        var d = sizes[0];
        mean = new double[d];
        std = new double[d];
        if (x.Length == 0)
        {
            Array.Fill(std, 1.0);
            return;
        }

        foreach (var row in x)
        {
            for (var j = 0; j < d; j++)
            {
                mean[j] += row[j];
            }
        }

        for (var j = 0; j < d; j++)
        {
            mean[j] /= x.Length;
        }

        foreach (var row in x)
        {
            for (var j = 0; j < d; j++)
            {
                std[j] += (row[j] - mean[j]) * (row[j] - mean[j]);
            }
        }

        for (var j = 0; j < d; j++)
        {
            std[j] = Math.Sqrt(std[j] / x.Length);
            if (std[j] < 1e-9)
            {
                std[j] = 1.0;
            }
        }
    }

    /// <summary>Прямий прохід: імовірності класів.</summary>
    /// <exception cref="ArgumentException">Розмір входу не збігається з першим шаром.</exception>
    public double[] Forward(double[] x) => ForwardAll(x)[^1];

    /// <summary>Номер найімовірнішого класу.</summary>
    public int Classify(double[] x)
    {
        var p = Forward(x);
        var best = 0;
        for (var k = 1; k < p.Length; k++)
        {
            if (p[k] > p[best])
            {
                best = k;
            }
        }

        return best;
    }

    /// <summary>Навчання міні-батч Adam на зваженій крос-ентропії.</summary>
    /// <returns>Середня втрата останньої епохи.</returns>
    /// <exception cref="ArgumentException">Різна кількість прикладів і міток або мітка поза діапазоном.</exception>
    public double Train(double[][] x, int[] y, TrainOptions options)
    {
        ArgumentNullException.ThrowIfNull(x);
        ArgumentNullException.ThrowIfNull(y);
        ArgumentNullException.ThrowIfNull(options);
        var classes = sizes[^1];
        if (x.Length != y.Length || y.Any(c => c < 0 || c >= classes))
        {
            throw new ArgumentException("Кількість прикладів і міток різна або мітка поза діапазоном.", nameof(y));
        }

        var classWeights = options.ClassWeights ?? Enumerable.Repeat(1.0, classes).ToArray();
        var layers = weights.Length;
        var gw = Shape(weights);
        var gb = biases.Select(b => new double[b.Length]).ToArray();
        var mw = Shape(weights);
        var vw = Shape(weights);
        var mb = biases.Select(b => new double[b.Length]).ToArray();
        var vb = biases.Select(b => new double[b.Length]).ToArray();
        const double beta1 = 0.9, beta2 = 0.999, eps = 1e-8;
        var step = 0;
#pragma warning disable CA5394 // не криптографічне використання: перемішування батчів
        var rng = new Random(options.Seed);
#pragma warning restore CA5394
        var order = Enumerable.Range(0, x.Length).ToArray();
        var lastLoss = 0.0;
        for (var epoch = 0; epoch < options.Epochs; epoch++)
        {
            Shuffle(order, rng);
            var epochLoss = 0.0;
            for (var start = 0; start < order.Length; start += options.BatchSize)
            {
                var end = Math.Min(order.Length, start + options.BatchSize);
                Clear(gw);
                Clear(gb);
                var batchWeight = 0.0;
                for (var bi = start; bi < end; bi++)
                {
                    var idx = order[bi];
                    var acts = ForwardAll(x[idx]);
                    var cw = classWeights[y[idx]];
                    batchWeight += cw;
                    var probs = acts[^1];
                    epochLoss += -cw * Math.Log(Math.Max(probs[y[idx]], 1e-12));
                    var delta = new double[probs.Length];
                    for (var k = 0; k < delta.Length; k++)
                    {
                        delta[k] = cw * (probs[k] - (k == y[idx] ? 1 : 0));
                    }

                    for (var l = layers - 1; l >= 0; l--)
                    {
                        var input = acts[l];
                        for (var o = 0; o < delta.Length; o++)
                        {
                            gb[l][o] += delta[o];
                            var row = gw[l][o];
                            for (var i = 0; i < input.Length; i++)
                            {
                                row[i] += delta[o] * input[i];
                            }
                        }

                        if (l == 0)
                        {
                            break;
                        }

                        var prevDelta = new double[input.Length];
                        for (var i = 0; i < input.Length; i++)
                        {
                            if (input[i] <= 0)
                            {
                                continue; // похідна ReLU
                            }

                            var s = 0.0;
                            for (var o = 0; o < delta.Length; o++)
                            {
                                s += weights[l][o][i] * delta[o];
                            }

                            prevDelta[i] = s;
                        }

                        delta = prevDelta;
                    }
                }

                step++;
                var scale = 1.0 / Math.Max(batchWeight, 1e-12);
                var c1 = 1 - Math.Pow(beta1, step);
                var c2 = 1 - Math.Pow(beta2, step);
                for (var l = 0; l < layers; l++)
                {
                    for (var o = 0; o < weights[l].Length; o++)
                    {
                        for (var i = 0; i < weights[l][o].Length; i++)
                        {
                            var g = gw[l][o][i] * scale;
                            mw[l][o][i] = (beta1 * mw[l][o][i]) + ((1 - beta1) * g);
                            vw[l][o][i] = (beta2 * vw[l][o][i]) + ((1 - beta2) * g * g);
                            weights[l][o][i] -= options.LearningRate * (mw[l][o][i] / c1) / (Math.Sqrt(vw[l][o][i] / c2) + eps);
                        }

                        var gbo = gb[l][o] * scale;
                        mb[l][o] = (beta1 * mb[l][o]) + ((1 - beta1) * gbo);
                        vb[l][o] = (beta2 * vb[l][o]) + ((1 - beta2) * gbo * gbo);
                        biases[l][o] -= options.LearningRate * (mb[l][o] / c1) / (Math.Sqrt(vb[l][o] / c2) + eps);
                    }
                }
            }

            lastLoss = epochLoss / Math.Max(1, x.Length);
        }

        return lastLoss;
    }

    /// <summary>Зберігає ваги й нормалізацію детерміновано.</summary>
    public void Save(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var temp = path + ".tmp";
        File.WriteAllText(temp, ToJson());
        File.Move(temp, path, overwrite: true);
    }

    /// <summary>JSON з архітектурою, нормалізацією і вагами (числа — у форматі round-trip, інваріантна культура).</summary>
    public string ToJson()
    {
        var root = new JsonObject
        {
            ["format"] = "telemetry-mlp-1",
            ["history"] = HistoryWindow,
            ["layers"] = new JsonArray([.. sizes.Select(s => (JsonNode)s)]),
            ["mean"] = Numbers(mean),
            ["std"] = Numbers(std),
            ["weights"] = new JsonArray([.. weights.Select(layer => (JsonNode)new JsonArray([.. layer.Select(row => (JsonNode)Numbers(row))]))]),
            ["biases"] = new JsonArray([.. biases.Select(b => (JsonNode)Numbers(b))]),
        };
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = false }) + "\n";
    }

    /// <summary>Відновлює мережу з JSON <see cref="ToJson"/>.</summary>
    /// <exception cref="InvalidDataException">Формат не той або розміри не узгоджені.</exception>
    public static Mlp FromJson(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        try
        {
            var root = JsonNode.Parse(json)!.AsObject();
            if ((string?)root["format"] != "telemetry-mlp-1")
            {
                throw new InvalidDataException("невідомий формат ваг");
            }

            var layerSizes = root["layers"]!.AsArray().Select(n => (int)n!).ToArray();
            var mlp = new Mlp(layerSizes, 0) { HistoryWindow = (int)root["history"]! };
            mlp.mean = Read(root["mean"]!, layerSizes[0]);
            mlp.std = Read(root["std"]!, layerSizes[0]);
            var w = root["weights"]!.AsArray();
            var b = root["biases"]!.AsArray();
            if (w.Count != layerSizes.Length - 1 || b.Count != layerSizes.Length - 1)
            {
                throw new InvalidDataException("кількість шарів ваг не збігається з архітектурою");
            }

            for (var l = 0; l < w.Count; l++)
            {
                var rows = w[l]!.AsArray();
                if (rows.Count != layerSizes[l + 1])
                {
                    throw new InvalidDataException("розмір шару ваг не збігається з архітектурою");
                }

                for (var o = 0; o < rows.Count; o++)
                {
                    mlp.weights[l][o] = Read(rows[o]!, layerSizes[l]);
                }

                mlp.biases[l] = Read(b[l]!, layerSizes[l + 1]);
            }

            return mlp;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or NullReferenceException or FormatException)
        {
            throw new InvalidDataException("файл ваг пошкоджено", ex);
        }
    }

    private double[][] ForwardAll(double[] x)
    {
        ArgumentNullException.ThrowIfNull(x);
        if (x.Length != sizes[0])
        {
            throw new ArgumentException($"очікується {sizes[0]} ознак, отримано {x.Length}", nameof(x));
        }

        var acts = new double[sizes.Length][];
        acts[0] = new double[x.Length];
        for (var j = 0; j < x.Length; j++)
        {
            acts[0][j] = (x[j] - mean[j]) / std[j];
        }

        for (var l = 0; l < weights.Length; l++)
        {
            var input = acts[l];
            var output = new double[sizes[l + 1]];
            for (var o = 0; o < output.Length; o++)
            {
                var s = biases[l][o];
                var row = weights[l][o];
                for (var i = 0; i < input.Length; i++)
                {
                    s += row[i] * input[i];
                }

                output[o] = l < weights.Length - 1 ? Math.Max(0, s) : s; // ReLU у прихованих шарах
            }

            acts[l + 1] = output;
        }

        Softmax(acts[^1]);
        return acts;
    }

    private static void Softmax(double[] z)
    {
        var max = z.Max();
        var sum = 0.0;
        for (var k = 0; k < z.Length; k++)
        {
            z[k] = Math.Exp(z[k] - max);
            sum += z[k];
        }

        for (var k = 0; k < z.Length; k++)
        {
            z[k] /= sum;
        }
    }

    private static double[][][] Shape(double[][][] like) =>
        like.Select(layer => layer.Select(row => new double[row.Length]).ToArray()).ToArray();

    private static void Clear(double[][][] a)
    {
        foreach (var layer in a)
        {
            foreach (var row in layer)
            {
                Array.Clear(row);
            }
        }
    }

    private static void Clear(double[][] a)
    {
        foreach (var row in a)
        {
            Array.Clear(row);
        }
    }

#pragma warning disable CA5394 // не криптографічне використання
    private static void Shuffle(int[] a, Random rng)
    {
        for (var i = a.Length - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (a[i], a[j]) = (a[j], a[i]);
        }
    }
#pragma warning restore CA5394

    private static JsonArray Numbers(double[] values) =>
        new([.. values.Select(v => (JsonNode)JsonValue.Create(v))]);

    private static double[] Read(JsonNode node, int expected)
    {
        var values = node.AsArray().Select(n => (double)n!).ToArray();
        return values.Length == expected ? values : throw new InvalidDataException("довжина вектора ваг не збігається з архітектурою");
    }
}
