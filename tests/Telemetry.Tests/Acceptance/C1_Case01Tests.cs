using Telemetry.Core;
using Telemetry.Huffman;
using Telemetry.Packet;
using Telemetry.Packing;
using Telemetry.Predict;
using Telemetry.Security;
using Telemetry.System;
using Telemetry.Tests.Infrastructure;

namespace Telemetry.Tests.Acceptance;

/// <summary>
/// C1 №1. Потік із одного кадру проходить і відновлюється побайтово.
/// Критерій: Sender → канал «спокійний» → Receiver на 1 кадрі; відновлені 12 байтів == вхідні; ref_01 читається.
/// </summary>
public class C1_Case01Tests
{
    [Fact]
    public void Case01_single_frame_reproduces_ref01_and_survives_calm_channel()
    {
        var reference = File.ReadAllBytes(TestData.Reference("ref_01_single_frame.bin"));
        var frame = new Frame(0, 1000, 215, -3, 1, 1);
        var sender = new Sender(
            FullRun.Config,
            new SessionCrypto(FullRun.Password),
            new ConstantClassifier(),
            new KnapsackDp(),
            new SenderOptions(
                Compression: CompressionMode.Huffman,
                DuplicateCritical: false,
                DeviceId: 1,
                Salt: reference[11..27],
                NoncePrefix: reference[27..35]));

        var plans = sender.BuildPackets([frame]);
        var packet = Assert.Single(sender.Seal(new Scheduler().Order(plans, 72.5)));
        Assert.Equal(107, packet.Length);
        Assert.Equal(reference, packet); // байт у байт з еталоном викладача

        var json = File.ReadAllText(TestData.Path("data", "team_config.json"));
        using var channel = new ChannelBridge(ChannelBridge.ScenarioJson(json, "спокійний"), FullRun.Config.Channel.Seed, TestData.Path("channel", "bridge.py"));
        var (delivered, _) = channel.Send(packet);
        Assert.True(delivered);

        var receiver = new Receiver(FullRun.Password, new HuffmanCodec());
        Assert.True(receiver.Accept(packet).Ok);
        var recovered = Assert.Single(receiver.Frames);
        Assert.Equal(frame.ToBytes(), recovered.ToBytes());
    }

    [Fact]
    public void Case01_full_system_on_one_frame()
    {
        var frame = new Frame(0, 1000, 215, -3, 1, 1);
        var system = new TelemetrySystem(FullRun.Config, new Scheduler());
        var sink = new ListChannel();
        system.Transmit([frame], sink, 72.5);
        Assert.Equal([frame], system.Receive(sink.Packets));
    }
}
