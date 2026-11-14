using System.Diagnostics;
using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using Telemetry.Core;
using Telemetry.Huffman;
using Telemetry.Packet;
using Telemetry.Packing;
using Telemetry.Predict;
using Telemetry.Security;
using Telemetry.System;

namespace Telemetry.Present;

/// <summary>
/// Дані для сторінки, які не залежать від прогону Runner: Гаффман на справжньому вікні сеансу,
/// жадібний рюкзак проти ДП, журнали ГА, результати перцептрона, розбір пакета і перевірка захисту.
/// Кожен розділ рахується окремо: збій одного не ламає решту.
/// </summary>
internal static partial class DemoData
{
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        NumberHandling = global::System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    public static string BuildJson()
    {
        var config = TeamConfig.Load(Repo.Config);
        Frame[]? frames = null;
        int[]? classes = null;
        string? framesError = null;
        try
        {
            frames = [.. File.ReadLines(Repo.Session)
                .Skip(1)
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .Select(Frame.ParseCsv)];
            classes = new Classifier().Predict(frames);
        }
        catch (Exception ex) when (ex is IOException or FormatException or InvalidDataException or NotImplementedException)
        {
            framesError = ex.Message;
        }

        object Needs(Func<Frame[], int[], object> build)
        {
            if (frames is null || classes is null)
            {
                throw new InvalidOperationException(framesError ?? "немає кадрів сеансу");
            }

            return build(frames, classes);
        }

        var bundle = new Dictionary<string, object>
        {
            ["session"] = Safe(() => Needs((f, _) => new
            {
                frames = f.Length,
                critical = f.Count(x => x.IsCritical),
                bRefBits = 96L * f.Length,
            })),
            ["huffman"] = Safe(() => Needs((f, c) => HuffmanSection(config, f, c))),
            ["knapsack"] = Safe(() => Needs((f, c) => KnapsackSection(config, f, c))),
            ["ga"] = Safe(GaSection),
            ["classifier"] = Safe(() => ClassifierSection(config)),
            ["ablation"] = Safe(AblationSection),
            ["packet"] = Safe(() => PacketSection(config)),
            ["checks"] = Safe(() => ChecksSection(config)),
            ["config"] = Safe(() => (object)new
            {
                window = config.Packing.CandidateWindowFrames,
                maxPacketBytes = config.Packing.MaxPacketBytes,
                maxPayloadBytes = config.Packing.MaxPayloadBytes,
                alpha = config.Metric.Alpha,
                beta = config.Metric.Beta,
                gamma = config.Metric.Gamma,
                tRef = config.Metric.TRefSeconds,
                thresholds = config.CriticalLossThresholds,
            }),
        };
        return JsonSerializer.Serialize(bundle, Json);
    }

    private static object Safe(Func<object> build)
    {
        try
        {
            return new { ok = true, data = build() };
        }
        catch (Exception ex) when (ex is IOException or FormatException or InvalidDataException or KeyNotFoundException
            or InvalidOperationException or NotImplementedException or ArgumentException or JsonException
            or UnauthorizedAccessException)
        {
            return new { ok = false, error = ex.Message };
        }
    }

    private sealed class Entry(Frame frame, int value, double key)
    {
        public Frame Frame { get; } = frame;

        public int Value { get; } = value;

        public double Key { get; } = key;
    }

    private static List<Entry> Queue(TeamConfig config, Frame[] frames, int[] classes, double priorityWeight)
    {
        var pr = config.Priorities;
        return [.. frames
            .Select((f, i) => new Entry(
                f,
                pr.Value(classes[i], f.IsCritical),
                (priorityWeight * pr.ForClass(classes[i])) + (f.IsCritical ? pr.CriticalBonus : 0)))
            .OrderByDescending(e => e.Key)
            .ThenBy(e => e.Frame.TimestampMs)];
    }

    private static int FrameBits(Frame frame, byte[] lengths) => frame.ToBytes().Sum(b => lengths[b]);

    private static long[] Frequencies(IEnumerable<byte> bytes)
    {
        var freq = new long[256];
        foreach (var b in bytes)
        {
            freq[b]++;
        }

        return freq;
    }

    private static object HuffmanSection(TeamConfig config, Frame[] frames, int[] classes)
    {
        var p = SenderOptions.DefaultParams;
        var window = Queue(config, frames, classes, p.PriorityWeight).Take(p.HuffmanWindowFrames).ToList();
        var bytes = Frame.Serialize([.. window.Select(e => e.Frame)]);
        var freq = Frequencies(bytes);
        var codec = new HuffmanCodec();
        var lengths = codec.BuildLengths(freq);
        var codes = HuffmanCodec.CanonicalCodes(lengths);
        var symbols = lengths.Count(l => l > 0);
        var totalBits = Enumerable.Range(0, 256).Sum(s => freq[s] * lengths[s]);

        var weights = window.Select(e => FrameBits(e.Frame, lengths)).ToArray();
        var values = window.Select(e => e.Value).ToArray();
        var capacity = config.Packing.CapacityBits(symbols);
        var chosen = new KnapsackDp().Select(weights, values, capacity);
        var chosenFrames = chosen.Select(i => window[i].Frame).ToArray();
        var huffmanPacket = PacketPlan.Overhead + PacketCodec.PayloadSize(PayloadFlags.None, lengths, chosenFrames);

        var rawFit = Math.Min(window.Count, (config.Packing.MaxPayloadBytes - PacketCodec.FixedPayloadBytes) / Frame.Size);
        var rawPacket = PacketPlan.Overhead + PacketCodec.FixedPayloadBytes + (rawFit * Frame.Size);

        return new
        {
            windowFrames = window.Count,
            symbols,
            tableBytes = 2 * symbols,
            entropy = NoCompression.Entropy(bytes),
            avgBits = (double)totalBits / bytes.Length,
            capacityBits = capacity,
            huffmanFrames = chosen.Length,
            huffmanValue = chosen.Sum(i => values[i]),
            huffmanPacketBytes = huffmanPacket,
            rawFrames = rawFit,
            rawValue = values.Take(rawFit).Sum(),
            rawPacketBytes = rawPacket,
            histogram = Enumerable.Range(1, HuffmanCodec.MaxCodeLength)
                .Select(l => new { length = l, symbols = lengths.Count(x => x == l) }),
            top = Enumerable.Range(0, 256)
                .Where(s => freq[s] > 0)
                .OrderByDescending(s => freq[s])
                .ThenBy(s => s)
                .Take(8)
                .Select(s => new
                {
                    symbol = s.ToString("X2", CultureInfo.InvariantCulture),
                    count = freq[s],
                    length = (int)lengths[s],
                    code = Convert.ToString(codes[s], 2).PadLeft(lengths[s], '0'),
                }),
            order = OrderComparison(frames, config.Packing.CandidateWindowFrames),
            ref04 = Ref04(),
            example = HuffmanExample(),
            params_ = new { window = p.HuffmanWindowFrames, threshold = p.NoCompressionThreshold, weight = p.PriorityWeight },
        };
    }

    private static object Ref04()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Repo.Path("data", "reference", "ref_04_session.json")));
        var frames = doc.RootElement.GetProperty("кадри").EnumerateArray()
            .Select(e => new Frame(
                e.GetProperty("timestamp_ms").GetUInt32(),
                e.GetProperty("altitude_dm").GetInt16(),
                e.GetProperty("temp_c10").GetInt16(),
                e.GetProperty("vibration").GetInt16(),
                e.GetProperty("mode").GetByte(),
                e.GetProperty("critical").GetByte()))
            .ToArray();
        var bytes = Frame.Serialize(frames);
        var codec = new HuffmanCodec();
        var lengths = codec.BuildLengths(Frequencies(bytes));
        var (stream, bits) = codec.Encode(bytes, lengths);
        var symbols = lengths.Count(l => l > 0);
        return new
        {
            frames = frames.Length,
            rawBits = bytes.Length * 8,
            rawPacketBits = 8 * (PacketPlan.Overhead + PacketCodec.FixedPayloadBytes + bytes.Length),
            streamBits = bits,
            tableBits = 16 * symbols,
            payloadBits = 8 * (PacketCodec.FixedPayloadBytes + (2 * symbols) + stream.Length),
            packetBits = 8 * new FileInfo(Repo.Path("data", "reference", "ref_04_session.bin")).Length,
            symbols,
            matchesReference = Convert.ToHexString(stream).Equals(doc.RootElement.GetProperty("bitstream_hex").GetString(), StringComparison.OrdinalIgnoreCase),
        };
    }

    private static object HuffmanExample()
    {
        var frequencies = new long[256];
        frequencies['A'] = 5;
        frequencies['B'] = 2;
        frequencies['C'] = 1;
        var lengths = new HuffmanCodec().BuildLengths(frequencies);
        var codes = HuffmanCodec.CanonicalCodes(lengths);
        return Enumerable.Range('A', 3).Select(value => (char)value).Select(symbol => new
        {
            symbol,
            count = frequencies[symbol],
            length = lengths[symbol],
            code = Convert.ToString(codes[symbol], 2).PadLeft(lengths[symbol], '0'),
        });
    }

    private static object[] OrderComparison(Frame[] frames, int windowSize)
    {
        var codec = new HuffmanCodec();
        var key = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();
        var nonce = new byte[CtrCipher.NonceSize];
        var ordered = frames.OrderBy(f => f.TimestampMs).ToArray();
        long raw = 0, compressFirst = 0, encryptFirst = 0, encryptFirstFallback = 0;
        for (var start = 0; start + windowSize <= ordered.Length; start += windowSize)
        {
            var plain = Frame.Serialize(ordered[start..(start + windowSize)]);
            var cipher = CtrCipher.Transform(key, nonce, plain);
            var rawSize = PacketCodec.FixedPayloadBytes + plain.Length;
            var plainSize = Math.Min(PayloadSize(codec, plain), rawSize);
            var cipherSize = PayloadSize(codec, cipher);
            raw += rawSize;
            compressFirst += plainSize;
            encryptFirst += cipherSize;
            encryptFirstFallback += Math.Min(cipherSize, rawSize);
        }

        return new object[]
        {
            new { name = "Без стиснення", bytes = raw, ratio = 1.0 },
            new { name = "Стиснути → зашифрувати", bytes = compressFirst, ratio = (double)raw / compressFirst },
            new { name = "Зашифрувати → стиснути", bytes = encryptFirst, ratio = (double)raw / encryptFirst },
            new { name = "Зашифрувати → стиснути (з NO_COMPRESSION)", bytes = encryptFirstFallback, ratio = (double)raw / encryptFirstFallback },
        };
    }

    private static int PayloadSize(HuffmanCodec codec, byte[] data)
    {
        var lengths = codec.BuildLengths(Frequencies(data));
        var (stream, _) = codec.Encode(data, lengths);
        return PacketCodec.FixedPayloadBytes + (2 * lengths.Count(l => l > 0)) + stream.Length;
    }

    private static object KnapsackSection(TeamConfig config, Frame[] frames, int[] classes)
    {
        var p = SenderOptions.DefaultParams;
        var queue = Queue(config, frames, classes, p.PriorityWeight);
        var codec = new HuffmanCodec();
        var greedy = new KnapsackGreedy();
        var dp = new KnapsackDp();
        var windows = new List<object>();
        long greedyTotal = 0, dpTotal = 0;
        double greedyMicros = 0, dpMicros = 0, worstGap = 0;
        var differing = 0;

        while (queue.Count > 0)
        {
            var window = queue.Take(p.HuffmanWindowFrames).ToList();
            var lengths = codec.BuildLengths(Frequencies(window.SelectMany(e => e.Frame.ToBytes())));
            var weights = window.Select(e => FrameBits(e.Frame, lengths)).ToArray();
            var values = window.Select(e => e.Value).ToArray();
            var capacity = config.Packing.CapacityBits(lengths.Count(l => l > 0));

            var t0 = Stopwatch.GetTimestamp();
            var g = greedy.Select(weights, values, capacity);
            greedyMicros += Stopwatch.GetElapsedTime(t0).TotalMicroseconds;
            var t1 = Stopwatch.GetTimestamp();
            var d = dp.Select(weights, values, capacity);
            dpMicros += Stopwatch.GetElapsedTime(t1).TotalMicroseconds;
            if (d.Length == 0)
            {
                break;
            }

            var gv = g.Sum(i => values[i]);
            var dv = d.Sum(i => values[i]);
            greedyTotal += gv;
            dpTotal += dv;
            if (dv != gv)
            {
                differing++;
            }

            if (dv > 0)
            {
                worstGap = Math.Max(worstGap, 100.0 * (dv - gv) / dv);
            }

            windows.Add(new { greedy = gv, dp = dv });
            var taken = d.Select(i => window[i]).ToHashSet();
            queue.RemoveAll(taken.Contains);
        }

        return new
        {
            packets = windows.Count,
            greedyTotal,
            dpTotal,
            greedyMicros,
            dpMicros,
            gapPercent = dpTotal == 0 ? 0 : 100.0 * (dpTotal - greedyTotal) / dpTotal,
            worstWindowGapPercent = worstGap,
            windowsDiffer = differing,
            windows,
        };
    }

    private static object GaSection()
    {
        static IEnumerable<object> Log(string file) =>
            File.ReadLines(Repo.Path("results", "ga", file)).Skip(1)
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .Select(l => l.Split(','))
                .Select(c => new
                {
                    generation = int.Parse(c[0], CultureInfo.InvariantCulture),
                    best = double.Parse(c[1], CultureInfo.InvariantCulture),
                    mean = double.Parse(c[2], CultureInfo.InvariantCulture),
                });

        var summary = File.ReadAllLines(Repo.Path("results", "ga", "summary.md"));
        return new
        {
            ga = Log("ga_log.csv"),
            random = Log("random_search_log.csv"),
            notes = summary.Where(l => l.StartsWith("Найкраще", StringComparison.Ordinal)
                || l.StartsWith("Параметри за замовчуванням", StringComparison.Ordinal)
                || l.StartsWith("Fitness", StringComparison.Ordinal)),
        };
    }

    private static object ClassifierSection(TeamConfig config)
    {
        var text = File.ReadAllText(Repo.Path("results", "classifier.md"));
        var tables = MarkdownTable.ParseAll(text);
        var seeds = tables[0].Skip(1).Select(r => new
        {
            seed = r[0],
            f1 = MarkdownTable.Number(r[1]),
            loss = MarkdownTable.Number(r[2]),
        });
        var matrix = tables[1].Skip(1).Select(r => new
        {
            label = r[0],
            cells = r.Skip(1).Select(c => (int)MarkdownTable.Number(c)).ToArray(),
        });
        var f1 = double.Parse(MacroF1().Match(text).Groups[1].Value, CultureInfo.InvariantCulture);
        return new
        {
            seeds,
            matrix,
            columns = tables[1][0].Skip(1),
            f1,
            f1Min = config.Perceptron.F1Min,
            majority = config.Perceptron.MajorityBaseline,
        };
    }

    [GeneratedRegex(@"macro-F1 = ([0-9.]+)")]
    private static partial Regex MacroF1();

    private static object AblationSection()
    {
        var tables = MarkdownTable.ParseAll(File.ReadAllText(Repo.Path("results", "ablation.md")));
        return tables[0].Skip(1).Select(r => new
        {
            variant = r[0],
            calm = MarkdownTable.Number(r[1]),
            work = MarkdownTable.Number(r[2]),
            emergency = MarkdownTable.Number(r[3]),
            mean = MarkdownTable.Number(r[4]),
        });
    }

    private static object PacketSection(TeamConfig config)
    {
        var bytes = File.ReadAllBytes(Repo.Path("data", "reference", "ref_01_single_frame.bin"));
        var header = PacketCodec.ReadHeader(bytes);
        var ctLength = (int)header.CiphertextLength;
        string Hex(int start, int len) => Convert.ToHexString(bytes, start, len);

        var receiver = new Receiver(config.SessionPassword, new HuffmanCodec());
        var result = receiver.Accept(bytes);
        var frame = receiver.Frames.Count > 0 ? receiver.Frames[0] : default;

        var json = JsonDocument.Parse(File.ReadAllText(Repo.Path("data", "reference", "ref_01_single_frame.json")));
        var table = json.RootElement.GetProperty("таблиця_кодів").EnumerateArray().Select(e => new
        {
            symbol = e.GetProperty("символ").GetInt32().ToString("X2", CultureInfo.InvariantCulture),
            length = e.GetProperty("довжина").GetInt32(),
            code = e.GetProperty("код").GetString(),
        }).ToArray();

        return new
        {
            total = bytes.Length,
            accepted = result.Ok,
            segments = new object[]
            {
                new { name = "magic", group = "header", start = 0, length = 4, hex = Hex(0, 4), note = "«TLM1»" },
                new { name = "version", group = "header", start = 4, length = 1, hex = Hex(4, 1), note = "1" },
                new { name = "device_id", group = "header", start = 5, length = 2, hex = Hex(5, 2), note = header.DeviceId.ToString(CultureInfo.InvariantCulture) },
                new { name = "packet_seq", group = "header", start = 7, length = 4, hex = Hex(7, 4), note = header.PacketSeq.ToString(CultureInfo.InvariantCulture) },
                new { name = "salt", group = "header", start = 11, length = 16, hex = Hex(11, 16), note = "сіль KDF" },
                new { name = "nonce", group = "header", start = 27, length = 12, hex = Hex(27, 12), note = "унікальний на пакет" },
                new { name = "ciphertext_len", group = "header", start = 39, length = 4, hex = Hex(39, 4), note = ctLength.ToString(CultureInfo.InvariantCulture) + " Б" },
                new { name = "шифротекст", group = "cipher", start = 43, length = ctLength, hex = Hex(43, ctLength), note = "AES-256-CTR" },
                new { name = "HMAC-SHA256", group = "mac", start = 43 + ctLength, length = 32, hex = Hex(43 + ctLength, 32), note = "над заголовком і шифротекстом" },
            },
            frame = result.Ok
                ? new { frame.TimestampMs, frame.AltitudeDm, frame.TempC10, frame.Vibration, frame.Mode, frame.Critical }
                : null,
            table,
            bitstream = json.RootElement.GetProperty("bitstream_hex").GetString(),
        };
    }

    private static object ChecksSection(TeamConfig config)
    {
        var dir = Repo.Path("data", "reference");
        var codec = new HuffmanCodec();
        var checks = new List<object>();

        foreach (var bin in Directory.EnumerateFiles(dir, "ref_*.bin").Order(StringComparer.Ordinal))
        {
            var receiver = new Receiver(config.SessionPassword, codec);
            var packet = File.ReadAllBytes(bin);
            var result = receiver.Accept(packet);
            checks.Add(new
            {
                file = Path.GetFileName(bin),
                size = packet.Length,
                expected = "None",
                got = result.Error.ToString(),
                ok = result.Ok,
                frames = receiver.Frames.Count,
            });
        }

        using var expectedDoc = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "expected_failures.json")));
        foreach (var item in expectedDoc.RootElement.EnumerateObject())
        {
            var expected = Enum.Parse<PacketError>(item.Value.GetProperty("очікуваний_код").GetString()!);
            var packet = File.ReadAllBytes(Path.Combine(dir, item.Name));
            var result = new Receiver(config.SessionPassword, codec).Accept(packet);
            checks.Add(new
            {
                file = item.Name,
                size = packet.Length,
                expected = expected.ToString(),
                got = result.Error.ToString(),
                ok = result.Error == expected && result.Payload is null,
                frames = 0,
            });
        }

        var original = File.ReadAllBytes(Path.Combine(dir, "ref_04_session.bin"));
        PacketError Try(byte[] packet, string password) => new Receiver(password, codec).Accept(packet).Error;
        byte[] Mutate(Action<byte[]> change)
        {
            var copy = (byte[])original.Clone();
            change(copy);
            return copy;
        }

        var tamper = new (string Title, PacketError Expected, PacketError Got)[]
        {
            ("Пакет без змін", PacketError.None, Try(original, config.SessionPassword)),
            ("Змінено один байт шифротексту", PacketError.E_HMAC, Try(Mutate(b => b[100] ^= 0x01), config.SessionPassword)),
            ("Змінено байт тега HMAC", PacketError.E_HMAC, Try(Mutate(b => b[^1] ^= 0x01), config.SessionPassword)),
            ("Невірний пароль", PacketError.E_HMAC, Try(original, "wrong-password")),
            ("Пакет обрізано на 9 байтів", PacketError.E_LENGTH, Try(original[..^9], config.SessionPassword)),
            ("Зіпсовано magic", PacketError.E_MAGIC, Try(Mutate(b => b[0] ^= 0xFF), config.SessionPassword)),
        };

        return new
        {
            refs = checks,
            tamper = tamper.Select(t => new
            {
                title = t.Title,
                expected = t.Expected.ToString(),
                got = t.Got.ToString(),
                ok = t.Expected == t.Got,
            }),
        };
    }
}
