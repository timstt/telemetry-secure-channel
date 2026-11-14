using Telemetry.Huffman;
using Telemetry.Packet;
using Telemetry.System;
using Telemetry.Tests.Infrastructure;

namespace Telemetry.Tests.Acceptance;

/// <summary>
/// C1 №2. Порожній потік не спричиняє аварійного завершення.
/// Критерій: 0 кадрів: без винятків, 0 відновлених кадрів, Q обчислено (R=0 → Q=0 за правилом метрики).
/// </summary>
public class C1_Case02Tests
{
    [Fact]
    public void Case02_empty_stream_does_not_crash()
    {
        var system = new TelemetrySystem(FullRun.Config, new Scheduler());
        var sink = new ListChannel();
        system.Transmit([], sink, 16.9);
        Assert.Empty(sink.Packets);
        Assert.Empty(system.Receive(sink.Packets));

        var q = Metric.Compute(FullRun.Config.Metric, 0, 0, 1.0, 0.01, receiverCrashed: false);
        Assert.InRange(q.Q, 0, 1);
        Assert.Equal(0, Metric.Compute(FullRun.Config.Metric, 0, 0, 0.0, 0.01, receiverCrashed: false).Q);
    }

    [Fact]
    public void Case02_ref02_with_zero_frames_is_read()
    {
        var packet = File.ReadAllBytes(TestData.Reference("ref_02_empty.bin"));
        Assert.Equal(89, packet.Length);
        var receiver = new Receiver(FullRun.Password, new HuffmanCodec());
        Assert.True(receiver.Accept(packet).Ok);
        Assert.Empty(receiver.Frames);
    }
}
