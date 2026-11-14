using System.Text.Json;
using Telemetry.Core;
using Telemetry.Huffman;
using Telemetry.Packet;

namespace Telemetry.Tools;

/// <summary>
/// check-ref: усі ref_*.bin приймаються і дають frame_count з ref_*.json; усі bad_*.bin дають код
/// з expected_failures.json. Код виходу 0 — усе збіглося (C1 №12). Власник: тімлід.
/// </summary>
internal static class CheckRefPackets
{
    public static int Run(string[] args)
    {
        var root = RepoPaths.Root();
        var dir = args.Length > 0 ? args[0] : Path.Combine(root, "data", "reference");
        var password = TeamConfig.Load(Path.Combine(root, "data", "team_config.json")).SessionPassword;
        var failures = 0;

        foreach (var bin in Directory.EnumerateFiles(dir, "ref_*.bin").Order(StringComparer.Ordinal))
        {
            var receiver = new Receiver(password, new HuffmanCodec());
            var packet = File.ReadAllBytes(bin);
            var result = receiver.Accept(packet);
            var json = Path.ChangeExtension(bin, ".json");
            int? expectedFrames = null;
            if (File.Exists(json))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(json));
                expectedFrames = doc.RootElement.GetProperty("frame_count").GetInt32();
            }

            var ok = result.Ok && (expectedFrames is null || expectedFrames == receiver.Frames.Count);
            failures += ok ? 0 : 1;
            Console.WriteLine($"{(ok ? "OK  " : "FAIL")} {Path.GetFileName(bin),-28} {packet.Length,5} Б  {result.Error,-9} кадрів {receiver.Frames.Count}{(expectedFrames is { } e ? $" (очікувано {e})" : "")}");
        }

        var expectedPath = Path.Combine(dir, "expected_failures.json");
        using (var doc = JsonDocument.Parse(File.ReadAllText(expectedPath)))
        {
            foreach (var p in doc.RootElement.EnumerateObject())
            {
                var expected = Enum.Parse<PacketError>(p.Value.GetProperty("очікуваний_код").GetString()!);
                var packet = File.ReadAllBytes(Path.Combine(dir, p.Name));
                var result = new Receiver(password, new HuffmanCodec()).Accept(packet);
                var ok = result.Error == expected && result.Payload is null;
                failures += ok ? 0 : 1;
                Console.WriteLine($"{(ok ? "OK  " : "FAIL")} {p.Name,-28} {packet.Length,5} Б  {result.Error,-9} (очікувано {expected})");
            }
        }

        Console.WriteLine(failures == 0 ? "Усі еталонні пакети оброблено правильно." : $"Розбіжностей: {failures}");
        return failures == 0 ? 0 : 1;
    }
}
