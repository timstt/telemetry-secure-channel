using System.Buffers.Binary;
using System.Text.Json;
using Telemetry.Tests.Infrastructure;

namespace Telemetry.Tests.Huffman;

internal sealed record ReferenceFile(
    int FrameCount,
    int Flags,
    (int Symbol, int Length, string Code)[] Table,
    byte[] Bitstream,
    byte[] FrameBytes)
{
    public static ReferenceFile Load(string name)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(TestData.Reference(name + ".json")));
        var root = doc.RootElement;

        var table = root.GetProperty("таблиця_кодів").EnumerateArray()
            .Select(e => (e.GetProperty("символ").GetInt32(), e.GetProperty("довжина").GetInt32(), e.GetProperty("код").GetString()!))
            .ToArray();

        var frameBytes = new List<byte>();
        foreach (var f in root.GetProperty("кадри").EnumerateArray())
        {
            var buf = new byte[12];
            BinaryPrimitives.WriteUInt32BigEndian(buf.AsSpan(0, 4), f.GetProperty("timestamp_ms").GetUInt32());
            BinaryPrimitives.WriteInt16BigEndian(buf.AsSpan(4, 2), f.GetProperty("altitude_dm").GetInt16());
            BinaryPrimitives.WriteInt16BigEndian(buf.AsSpan(6, 2), f.GetProperty("temp_c10").GetInt16());
            BinaryPrimitives.WriteInt16BigEndian(buf.AsSpan(8, 2), f.GetProperty("vibration").GetInt16());
            buf[10] = f.GetProperty("mode").GetByte();
            buf[11] = f.GetProperty("critical").GetByte();
            frameBytes.AddRange(buf);
        }

        return new ReferenceFile(
            root.GetProperty("frame_count").GetInt32(),
            root.GetProperty("flags").GetInt32(),
            table,
            Convert.FromHexString(root.GetProperty("bitstream_hex").GetString()!),
            [.. frameBytes]);
    }

    public byte[] Lengths()
    {
        var lengths = new byte[256];
        foreach (var (symbol, length, _) in Table)
        {
            lengths[symbol] = (byte)length;
        }

        return lengths;
    }
}
