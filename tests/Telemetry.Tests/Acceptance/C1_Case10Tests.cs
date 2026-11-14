using Telemetry.Core;
using Telemetry.Huffman;
using Telemetry.Packet;
using Telemetry.Tests.Infrastructure;

namespace Telemetry.Tests.Acceptance;

/// <summary>
/// C1 №10. Пакет зі зміненим байтом і зі зміненим HMAC відхиляються.
/// Критерій: bad_01 → E_HMAC, bad_02 → E_HMAC; власний пакет з інвертованим байтом у кожній позиції → відмова.
/// </summary>
public class C1_Case10Tests
{
    [Theory]
    [InlineData("bad_01_tampered_byte.bin")]
    [InlineData("bad_02_tampered_tag.bin")]
    public void Case10_tampered_byte_and_tag_are_E_HMAC(string file)
    {
        var r = new Receiver(FullRun.Password, new HuffmanCodec()).Accept(File.ReadAllBytes(TestData.Reference(file)));
        Assert.Equal(PacketError.E_HMAC, r.Error);
        Assert.Null(r.Payload);
    }

    [Fact]
    public void Case10_flipping_any_byte_of_a_packet_is_rejected()
    {
        var packet = File.ReadAllBytes(TestData.Reference("ref_06_class_table.bin"));
        var receiver = new Receiver(FullRun.Password, new HuffmanCodec());
        for (var i = 0; i < packet.Length; i++)
        {
            var copy = (byte[])packet.Clone();
            copy[i] = (byte)~copy[i];
            Assert.False(receiver.Accept(copy).Ok, $"байт {i}");
        }

        Assert.Empty(receiver.Frames);
    }
}
