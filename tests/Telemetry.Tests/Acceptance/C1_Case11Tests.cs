using Telemetry.Core;
using Telemetry.Huffman;
using Telemetry.Packet;
using Telemetry.Tests.Infrastructure;

namespace Telemetry.Tests.Acceptance;

/// <summary>
/// C1 №11. Невірний пароль → відмова; обрізаний пакет → відмова без падіння.
/// Критерій: Невірний пароль → E_HMAC, payload == null; bad_03 → E_LENGTH; усі префікси пакета → відмова без винятку.
/// </summary>
public class C1_Case11Tests
{
    [Fact]
    public void Case11_wrong_password_is_rejected_not_garbage()
    {
        var receiver = new Receiver("algo2025", new HuffmanCodec());
        foreach (var name in new[] { "ref_01_single_frame.bin", "ref_04_session.bin" })
        {
            var r = receiver.Accept(File.ReadAllBytes(TestData.Reference(name)));
            Assert.Equal(PacketError.E_HMAC, r.Error);
            Assert.Null(r.Payload);
        }

        Assert.Empty(receiver.Frames);
    }

    [Fact]
    public void Case11_truncated_packets_are_rejected_without_crash()
    {
        var receiver = new Receiver(FullRun.Password, new HuffmanCodec());
        Assert.Equal(PacketError.E_LENGTH, receiver.Accept(File.ReadAllBytes(TestData.Reference("bad_03_truncated.bin"))).Error);
        var packet = File.ReadAllBytes(TestData.Reference("ref_04_session.bin"));
        for (var n = 0; n < packet.Length; n++)
        {
            var r = receiver.Accept(packet[..n]);
            Assert.Equal(n < 75 ? PacketError.E_SHORT : PacketError.E_LENGTH, r.Error);
        }

        Assert.Empty(receiver.Frames);
    }
}
