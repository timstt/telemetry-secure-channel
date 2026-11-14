using System.Buffers.Binary;
using Telemetry.Core;
using Telemetry.Huffman;
using Telemetry.Packet;
using Telemetry.Packing;
using Telemetry.Predict;
using Telemetry.Security;
using Telemetry.System;
using Telemetry.Tests.Infrastructure;

namespace Telemetry.Tests.SystemZone;

/// <summary>Передавач (зона тімліда): інваріанти пакетів, пастки 18–19, C2 п.4–5, seq після планувальника.</summary>
public class SenderTests
{
    private static readonly Kdf SharedKdf = new();
    private static readonly TeamConfig Config = TeamConfig.Load(TestData.Path("data", "team_config.json"));
    private static readonly Lazy<Frame[]> Session = new(() =>
        [.. File.ReadLines(TestData.Path("data", "session.csv")).Skip(1).Where(l => l.Length > 0).Select(Frame.ParseCsv)]);

    private static Sender NewSender(SenderOptions? options = null, TeamConfig? config = null, IClassifier? classifier = null) =>
        new(config ?? Config, new SessionCrypto(Config.SessionPassword, SharedKdf), classifier ?? new Classifier(), new KnapsackDp(), options);

    private static Payload Decode(PacketPlan plan) => PacketCodec.ReadPayload(plan.Payload, new HuffmanCodec(), PayloadFlags.Duplication);

    [Fact]
    public void Session_packets_respect_limit_and_carry_every_frame_exactly_once()
    {
        var sender = NewSender(new SenderOptions(DuplicateCritical: false));
        var plans = sender.BuildPackets(Session.Value);
        Assert.All(plans, p => Assert.True(p.SizeBits <= 8 * Config.Packing.MaxPacketBytes));
        Assert.Equal(Session.Value.OrderBy(f => f.TimestampMs), plans.SelectMany(p => Decode(p).Frames).OrderBy(f => f.TimestampMs));
        Assert.All(plans, p => Assert.Equal(p.Frames, Decode(p).Frames));
        Assert.All(plans, p => Assert.Equal(0, (int)(Decode(p).Flags & (PayloadFlags.ClassTable | PayloadFlags.Duplication | PayloadFlags.Prediction))));
        Assert.Equal(plans.Length, sender.HuffmanPackets + sender.RawPackets);
        Assert.True(sender.HuffmanPackets > 0);
    }

    [Fact]
    public void Duplicates_are_only_critical_frames_in_flagged_packets()
    {
        var plans = NewSender().BuildPackets(Session.Value);
        var dup = plans.Where(p => Decode(p).Flags.HasFlag(PayloadFlags.Duplication)).ToList();
        Assert.NotEmpty(dup);
        Assert.All(dup, p => Assert.All(p.Frames, f => Assert.True(f.IsCritical)));
        Assert.Equal(Session.Value.Count(f => f.IsCritical), dup.Sum(p => p.Frames.Length));
        Assert.All(dup, p => Assert.True(Decode(p).Flags.HasFlag(PayloadFlags.NoCompression)));
    }

    [Fact]
    public void Trap18_first_packets_hold_more_critical_frames_than_session_share()
    {
        var plans = NewSender(new SenderOptions(DuplicateCritical: false)).BuildPackets(Session.Value);
        var share = Session.Value.Count(f => f.IsCritical) / (double)Session.Value.Length;
        var first = plans.Take(5).SelectMany(p => p.Frames).ToList();
        Assert.True(first.Count(f => f.IsCritical) / (double)first.Count >= share);
    }

    [Fact]
    public void Trap19_auto_mode_is_never_worse_in_value_per_bit_than_forced_modes_on_first_packet()
    {
        double Density(PacketPlan p) => p.TotalValue / (double)p.SizeBits;
        var frames = Session.Value[..64];
        var auto = NewSender(new SenderOptions(DuplicateCritical: false)).BuildPackets(frames)[0];
        var huffman = NewSender(new SenderOptions(Compression: CompressionMode.Huffman, DuplicateCritical: false)).BuildPackets(frames)[0];
        var raw = NewSender(new SenderOptions(Compression: CompressionMode.None, DuplicateCritical: false)).BuildPackets(frames)[0];
        Assert.True(Density(auto) >= Math.Max(Density(huffman), Density(raw)) - 1e-12);
    }

    [Fact]
    public void C2_5_high_entropy_frames_are_sent_without_compression()
    {
        var rng = new Random(1);
        var frames = Enumerable.Range(0, 200).Select(i =>
        {
            var b = new byte[12];
            rng.NextBytes(b);
            b[10] = (byte)(b[10] & 7);
            b[11] = (byte)(b[11] & 1);
            return Frame.Read(b);
        }).ToArray();
        var sender = NewSender(new SenderOptions(DuplicateCritical: false, Params: new Params(64, 7.0, 1.0)), classifier: new ConstantClassifier());
        var plans = sender.BuildPackets(frames);
        Assert.All(plans, p => Assert.True(Decode(p).Flags.HasFlag(PayloadFlags.NoCompression)));
        Assert.Equal(0, sender.HuffmanPackets);
    }

    [Fact]
    public void C2_4_tiny_packet_limit_falls_back_to_raw_without_infinite_loop()
    {
        var json = File.ReadAllText(TestData.Path("data", "team_config.json"))
            .Replace("\"max_packet_bytes\": 512", "\"max_packet_bytes\": 150", StringComparison.Ordinal)
            .Replace("\"max_payload_bytes\": 437", "\"max_payload_bytes\": 75", StringComparison.Ordinal);
        var tiny = TeamConfig.Parse(json);
        var sender = NewSender(new SenderOptions(DuplicateCritical: false), tiny);
        var plans = sender.BuildPackets(Session.Value[..100]);
        Assert.Equal(5, sender.MaxRawFrames);
        Assert.All(plans, p => Assert.True(p.SizeBits <= 8 * 150));
        Assert.Equal(100, plans.Sum(p => p.Frames.Length));

        var none = TeamConfig.Parse(json
            .Replace("\"max_packet_bytes\": 150", "\"max_packet_bytes\": 90", StringComparison.Ordinal)
            .Replace("\"max_payload_bytes\": 75", "\"max_payload_bytes\": 15", StringComparison.Ordinal));
        Assert.Throws<ArgumentException>(() => NewSender(config: none));
    }

    [Fact]
    public void Block_toggles_no_compression_and_fifo_fill()
    {
        var frames = Session.Value[..300];
        var raw = NewSender(new SenderOptions(Compression: CompressionMode.None, DuplicateCritical: false)).BuildPackets(frames);
        Assert.All(raw, p => Assert.True(Decode(p).Flags.HasFlag(PayloadFlags.NoCompression)));
        Assert.Equal(300, raw.Sum(p => p.Frames.Length));

        var fifo = NewSender(new SenderOptions(Compression: CompressionMode.Huffman, UseKnapsack: false, DuplicateCritical: false)).BuildPackets(frames);
        Assert.Equal(300, fifo.Sum(p => p.Frames.Length));
        Assert.All(fifo, p => Assert.True(p.SizeBits <= 8 * Config.Packing.MaxPacketBytes));
    }

    [Fact]
    public void Seal_assigns_seq_in_send_order_with_unique_nonces()
    {
        var salt = Enumerable.Range(0, 16).Select(i => (byte)i).ToArray();
        var prefix = new byte[] { 9, 8, 7, 6, 5, 4, 3, 2 };
        var sender = NewSender(new SenderOptions(Salt: salt, NoncePrefix: prefix, DeviceId: 77));
        var plans = sender.BuildPackets(Session.Value[..200]);
        var ordered = new Scheduler().Order(plans, 16.9);
        var packets = sender.Seal(ordered).ToList();
        var receiver = new Receiver(new SessionCrypto(Config.SessionPassword, SharedKdf), new HuffmanCodec(), PayloadFlags.Duplication);
        for (var i = 0; i < packets.Count; i++)
        {
            var header = PacketCodec.ReadHeader(packets[i]);
            Assert.Equal((uint)i, header.PacketSeq);
            Assert.Equal((ushort)77, header.DeviceId);
            Assert.Equal(salt, header.Salt);
            Assert.Equal(prefix, header.Nonce[..8]);
            Assert.Equal((uint)i, BinaryPrimitives.ReadUInt32BigEndian(header.Nonce.AsSpan(8)));
            Assert.Equal(ordered[i].SizeBits, 8L * packets[i].Length); // розмір не залежить від seq
            Assert.True(receiver.Accept(packets[i]).Ok);
        }

        Assert.Equal(Session.Value[..200].OrderBy(f => f.TimestampMs), receiver.Frames);
    }

    [Fact]
    public void Validation_and_empty_input()
    {
        Assert.Empty(NewSender().BuildPackets([]));
        Assert.Throws<ArgumentException>(() => NewSender(new SenderOptions(Salt: new byte[3])));
        Assert.Throws<ArgumentException>(() => NewSender(new SenderOptions(NoncePrefix: new byte[3])));
        Assert.Throws<InvalidOperationException>(() => NewSender(classifier: new WrongClassifier()).BuildPackets(Session.Value[..3]));
        Assert.Equal(new Params(32, 7.5, 1.0), new SenderOptions().EffectiveParams);
    }

    [Fact]
    public void Disabled_blocks_still_deliver_everything_through_full_system()
    {
        var frames = Session.Value[..400];
        var options = new SystemOptions(new SenderOptions(Compression: CompressionMode.None), UseClassifier: false, UseDpPacker: false);
        var system = new TelemetrySystem(Config, new FifoScheduler(), options);
        var sink = new ListChannel();
        system.Transmit(frames, sink, 72.5);
        Assert.NotEmpty(system.LastPlans);
        Assert.Equal(frames.OrderBy(f => f.TimestampMs), system.Receive(sink.Packets));
    }

    private sealed class WrongClassifier : IClassifier
    {
        public int[] Predict(Frame[] frames) => [];
    }
}
