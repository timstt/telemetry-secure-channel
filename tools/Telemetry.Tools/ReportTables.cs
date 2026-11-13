using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Telemetry.Core;
using Telemetry.Huffman;
using Telemetry.Packing;

namespace Telemetry.Tools;

/// <summary>Таблиці звіту, що рахуються з реальних даних сеансу (data/session.csv).</summary>
internal static class ReportTables
{
    private const int FrameSize = 12;

    public static int Run()
    {
        var root = FindRoot();
        var config = TeamConfig.Load(Path.Combine(root, "data", "team_config.json"));
        var session = ReadSession(Path.Combine(root, "data", "session.csv"));

        var report = new StringBuilder();
        report.AppendLine(CultureInfo.InvariantCulture, $"Кадрів у сеансі: {session.Count}, критичних: {session.Count(f => f.Critical == 1)}.");
        report.AppendLine();
        report.Append(CompressionOrderTable(session, config));
        report.AppendLine();
        report.Append(GreedyTable(session, config));

        var text = report.ToString();
        Console.Write(text);

        var outDir = Path.Combine(root, "out");
        Directory.CreateDirectory(outDir);
        var target = Path.Combine(outDir, "report.md");
        var temp = target + ".tmp";
        File.WriteAllText(temp, text, Encoding.UTF8);
        File.Move(temp, target, overwrite: true);
        return 0;
    }

    private static string CompressionOrderTable(List<SessionFrame> session, TeamConfig config)
    {
        var window = config.Packing.CandidateWindowFrames;
        var codec = new HuffmanCodec();
        var key = RandomNumberGenerator.GetBytes(32);
        var nonce = new byte[12];

        long rawBytes = 0, compressFirst = 0, encryptFirst = 0, encryptFirstWithFallback = 0;
        var windows = 0;
        var ordered = session.OrderBy(f => f.Timestamp).ToList();
        for (var start = 0; start + window <= ordered.Count; start += window)
        {
            var plain = Serialize(ordered.Skip(start).Take(window));
            var cipher = CtrXor(key, nonce, plain);

            var plainSize = PayloadSize(codec, plain);
            var cipherSize = PayloadSize(codec, cipher);
            var rawSize = 14 + plain.Length;

            rawBytes += rawSize;
            compressFirst += Math.Min(plainSize, rawSize);
            encryptFirst += cipherSize;
            encryptFirstWithFallback += Math.Min(cipherSize, rawSize);
            windows++;
        }

        var sb = new StringBuilder();
        sb.AppendLine("Таблиця 3. Порядок «стиснути → зашифрувати» проти «зашифрувати → стиснути»");
        sb.AppendLine(CultureInfo.InvariantCulture, $"(вікон по {window} кадрів: {windows}; розмір навантаження в байтах, шифр AES-256-CTR без доповнення)");
        sb.AppendLine();
        sb.AppendLine("| Варіант | Σ байтів | Коефіцієнт стиснення |");
        sb.AppendLine("|---|---:|---:|");
        sb.AppendLine(CultureInfo.InvariantCulture, $"| без стиснення | {rawBytes} | 1.000 |");
        sb.AppendLine(CultureInfo.InvariantCulture, $"| стиснути → зашифрувати | {compressFirst} | {(double)rawBytes / compressFirst:F3} |");
        sb.AppendLine(CultureInfo.InvariantCulture, $"| зашифрувати → стиснути | {encryptFirst} | {(double)rawBytes / encryptFirst:F3} |");
        sb.AppendLine(CultureInfo.InvariantCulture, $"| зашифрувати → стиснути (з NO_COMPRESSION) | {encryptFirstWithFallback} | {(double)rawBytes / encryptFirstWithFallback:F3} |");
        return sb.ToString();
    }

    private static string GreedyTable(List<SessionFrame> session, TeamConfig config)
    {
        var codec = new HuffmanCodec();
        var greedy = new KnapsackGreedy();
        var dp = new KnapsackDp();
        var windowSize = config.Packing.CandidateWindowFrames;

        var queue = session
            .Select(f => (Frame: f, Value: config.Priorities.Value(f.Label, f.Critical == 1)))
            .OrderByDescending(x => x.Value)
            .ThenBy(x => x.Frame.Timestamp)
            .ToList();

        long greedyValue = 0, dpValue = 0;
        double greedyTicks = 0, dpTicks = 0;
        var packets = 0;
        var dpAvailable = true;
        var worstGap = 0.0;

        while (queue.Count > 0)
        {
            var window = queue.Take(windowSize).ToList();
            var frames = window.Select(x => Serialize([x.Frame])).ToList();
            var freq = new long[256];
            foreach (var b in frames.SelectMany(f => f))
            {
                freq[b]++;
            }

            var lengths = codec.BuildLengths(freq);
            var nsym = lengths.Count(l => l > 0);
            var weights = frames.Select(f => f.Sum(b => lengths[b])).ToArray();
            var values = window.Select(x => x.Value).ToArray();
            var capacity = config.Packing.CapacityBits(nsym);

            var t0 = Stopwatch.GetTimestamp();
            var chosen = greedy.Select(weights, values, capacity);
            greedyTicks += Stopwatch.GetElapsedTime(t0).TotalMicroseconds;
            if (chosen.Length == 0)
            {
                break;
            }

            var gv = chosen.Sum(i => values[i]);
            greedyValue += gv;

            if (dpAvailable)
            {
                try
                {
                    var t1 = Stopwatch.GetTimestamp();
                    var optimal = dp.Select(weights, values, capacity);
                    dpTicks += Stopwatch.GetElapsedTime(t1).TotalMicroseconds;
                    var ov = optimal.Sum(i => values[i]);
                    dpValue += ov;
                    if (ov > 0)
                    {
                        worstGap = Math.Max(worstGap, 1.0 - ((double)gv / ov));
                    }
                }
                catch (NotImplementedException)
                {
                    dpAvailable = false;
                }
            }

            var taken = chosen.ToHashSet();
            queue = window.Where((_, i) => !taken.Contains(i)).Concat(queue.Skip(windowSize)).ToList();
            packets++;
        }

        var sb = new StringBuilder();
        sb.AppendLine("Таблиця 1 (жадібний бік). Рюкзак на вікнах черги сеансу");
        sb.AppendLine(CultureInfo.InvariantCulture, $"(пакетів: {packets}; клас кадру — поле label сеансу, поки не підключено класифікатор)");
        sb.AppendLine();
        sb.AppendLine("| Метод | Σ цінність | Час, мкс |");
        sb.AppendLine("|---|---:|---:|");
        sb.AppendLine(CultureInfo.InvariantCulture, $"| жадібний | {greedyValue} | {greedyTicks:F0} |");
        if (dpAvailable)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"| ДП | {dpValue} | {dpTicks:F0} |");
            sb.AppendLine(CultureInfo.InvariantCulture, $"| похибка жадібного (сума) | {100.0 * (dpValue - greedyValue) / Math.Max(1, dpValue):F2} % | |");
            sb.AppendLine(CultureInfo.InvariantCulture, $"| найгірше вікно | {100.0 * worstGap:F2} % | |");
        }
        else
        {
            sb.AppendLine("| ДП | — | — |");
        }

        return sb.ToString();
    }

    private static int PayloadSize(HuffmanCodec codec, byte[] data)
    {
        var freq = new long[256];
        foreach (var b in data)
        {
            freq[b]++;
        }

        var lengths = codec.BuildLengths(freq);
        var (stream, _) = codec.Encode(data, lengths);
        return 14 + (2 * lengths.Count(l => l > 0)) + stream.Length;
    }

    private static byte[] CtrXor(byte[] key, byte[] nonce, byte[] data)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        var result = new byte[data.Length];
        var counter = new byte[16];
        var block = new byte[16];
        nonce.CopyTo(counter, 0);
        for (var offset = 0; offset < data.Length; offset += 16)
        {
            BinaryPrimitives.WriteUInt32BigEndian(counter.AsSpan(12), (uint)((offset / 16) + 1));
            aes.EncryptEcb(counter, block, PaddingMode.None);
            for (var i = 0; i < 16 && offset + i < data.Length; i++)
            {
                result[offset + i] = (byte)(data[offset + i] ^ block[i]);
            }
        }

        return result;
    }

    private static byte[] Serialize(IEnumerable<SessionFrame> frames)
    {
        var list = frames.ToList();
        var buffer = new byte[list.Count * FrameSize];
        for (var i = 0; i < list.Count; i++)
        {
            var span = buffer.AsSpan(i * FrameSize, FrameSize);
            BinaryPrimitives.WriteUInt32BigEndian(span[..4], list[i].Timestamp);
            BinaryPrimitives.WriteInt16BigEndian(span.Slice(4, 2), list[i].Altitude);
            BinaryPrimitives.WriteInt16BigEndian(span.Slice(6, 2), list[i].Temperature);
            BinaryPrimitives.WriteInt16BigEndian(span.Slice(8, 2), list[i].Vibration);
            span[10] = list[i].Mode;
            span[11] = list[i].Critical;
        }

        return buffer;
    }

    private static List<SessionFrame> ReadSession(string path)
    {
        var frames = new List<SessionFrame>();
        foreach (var line in File.ReadLines(path).Skip(1))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var p = line.Split(',');
            frames.Add(new SessionFrame(
                uint.Parse(p[0], CultureInfo.InvariantCulture),
                short.Parse(p[1], CultureInfo.InvariantCulture),
                short.Parse(p[2], CultureInfo.InvariantCulture),
                short.Parse(p[3], CultureInfo.InvariantCulture),
                byte.Parse(p[4], CultureInfo.InvariantCulture),
                byte.Parse(p[5], CultureInfo.InvariantCulture),
                int.Parse(p[6], CultureInfo.InvariantCulture)));
        }

        return frames;
    }

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Telemetry.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new DirectoryNotFoundException("Не знайдено Telemetry.sln");
    }

    private sealed record SessionFrame(uint Timestamp, short Altitude, short Temperature, short Vibration, byte Mode, byte Critical, int Label);
}
