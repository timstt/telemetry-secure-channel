using System.Text.Json;
using Telemetry.Core;
using Telemetry.Huffman;
using Telemetry.Packet;
using Telemetry.Tests.Infrastructure;

namespace Telemetry.Tests.Packet;

/// <summary>Пастки 4–6, 15, 16 (зона Колі): порядок перевірок приймача і коди відмови.</summary>
public class ReceiverTrapTests
{
    private static Receiver NewReceiver(string password = "algo2026") => new(password, new HuffmanCodec());

    public static TheoryData<string, string> BadPackets()
    {
        var data = new TheoryData<string, string>();
        using var doc = JsonDocument.Parse(File.ReadAllText(TestData.Reference("expected_failures.json")));
        foreach (var p in doc.RootElement.EnumerateObject())
        {
            data.Add(p.Name, p.Value.GetProperty("очікуваний_код").GetString()!);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(BadPackets))]
    public void Bad_packets_give_expected_code(string file, string code)
    {
        var r = NewReceiver().Accept(File.ReadAllBytes(TestData.Reference(file)));
        Assert.Equal(Enum.Parse<PacketError>(code), r.Error);
        Assert.Null(r.Payload);
    }

    [Fact]
    public void Trap05_oversized_ref04_is_accepted()
    {
        var r = NewReceiver().Accept(File.ReadAllBytes(TestData.Reference("ref_04_session.bin")));
        Assert.True(r.Ok); // 771 Б > max_packet_bytes = 512 — це НЕ підстава для відмови
    }

    [Fact]
    public void Trap06_wrong_password_is_E_HMAC_not_garbage()
    {
        var r = NewReceiver("wrong").Accept(File.ReadAllBytes(TestData.Reference("ref_01_single_frame.bin")));
        Assert.Equal(PacketError.E_HMAC, r.Error);
        Assert.Null(r.Payload);
    }

    [Fact]
    public void Trap06_short_packet_is_E_SHORT_without_exception()
    {
        Assert.Equal(PacketError.E_SHORT, NewReceiver().Accept(new byte[74]).Error);
        Assert.Equal(PacketError.E_SHORT, NewReceiver().Accept([]).Error);
    }

    [Fact]
    public void Trap15_unknown_extension_flag_is_E_PAYLOAD()
    {
        var header = File.ReadAllBytes(TestData.Reference("ref_01_single_frame.bin"))[..43];
        foreach (byte flag in new byte[] { 0x01, 0x02, 0x03 })
        {
            var plaintext = TestData.Hex("0001 00 00 0007 0001 0103 0304 D704 E804 FD04 FF03 0000000C 00000004 0CE6DF90");
            plaintext[2] = flag;
            var packet = new Telemetry.Security.SessionCrypto("algo2026").Seal(header, plaintext);
            var r = NewReceiver().Accept(packet);
            Assert.Equal(PacketError.E_PAYLOAD, r.Error);
            Assert.Null(r.Payload);
        }
    }

    [Fact]
    public void Trap16_duplicates_and_reordering_do_not_break_receiver()
    {
        var rx = NewReceiver();
        var a = File.ReadAllBytes(TestData.Reference("ref_05_no_compression.bin"));
        var b = File.ReadAllBytes(TestData.Reference("ref_01_single_frame.bin"));
        rx.Accept(a);
        rx.Accept(b);
        rx.Accept(a); // дублікат seq ігнорується
        var ts = rx.Frames.Select(f => f.TimestampMs).ToArray();
        Assert.Equal(ts.Order().ToArray(), ts);
    }

    [Theory]
    [InlineData("ref_01_single_frame")]
    [InlineData("ref_02_empty")]
    [InlineData("ref_03_degenerate")]
    [InlineData("ref_04_session")]
    [InlineData("ref_05_no_compression")]
    [InlineData("ref_06_class_table")]
    public void Reference_packets_decode_to_frames_from_json(string name)
    {
        var rx = NewReceiver();
        Assert.True(rx.Accept(File.ReadAllBytes(TestData.Reference(name + ".bin"))).Ok);
        using var doc = JsonDocument.Parse(File.ReadAllText(TestData.Reference(name + ".json")));
        Assert.Equal(doc.RootElement.GetProperty("frame_count").GetInt32(), rx.Frames.Count);
    }
}
