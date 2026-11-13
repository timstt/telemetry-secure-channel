using Telemetry.Core;

namespace Telemetry.Tests.Integration;

/// <summary>Інваріанти типів-контрактів, що не залежать від реалізації зон.</summary>
public class ContractTests
{
    [Fact]
    public void Packet_size_bits_includes_header_and_hmac()
    {
        var plan = new PacketPlan(0, new byte[32], [], 0);
        Assert.Equal(8L * 107, plan.SizeBits); // ref_01: 43 + 32 + 32
    }

    [Fact]
    public void Open_result_fail_has_no_payload()
    {
        var r = OpenResult.Fail(PacketError.E_HMAC);
        Assert.False(r.Ok);
        Assert.Null(r.Payload);
        Assert.True(new OpenResult(PacketError.None, 0, []).Ok);
    }

    [Fact]
    public void Frame_critical_flag()
    {
        Assert.True(new Frame(0, 1000, 215, -3, 1, 1).IsCritical);
        Assert.False(new Frame(0, 0, 0, 0, 0, 0).IsCritical);
        Assert.Equal(12, Frame.Size);
    }

    [Fact]
    public void Evolution_log_starts_empty() => Assert.Empty(new EvolutionLog().Generations);
}

public class ErrorTypeTests
{
    [Fact]
    public void Packet_format_exception_carries_code_without_content()
    {
        var ex = new Telemetry.Packet.PacketFormatException(PacketError.E_CODE);
        Assert.Equal(PacketError.E_CODE, ex.Code);
        Assert.Equal("E_CODE", ex.Message);
        Assert.Equal(PacketError.E_PAYLOAD, new Telemetry.Packet.PacketFormatException().Code);
        Assert.Equal(PacketError.E_PAYLOAD, new Telemetry.Packet.PacketFormatException("m").Code);
        var inner = new InvalidOperationException();
        Assert.Same(inner, new Telemetry.Packet.PacketFormatException("m", inner).InnerException);
    }

    [Fact]
    public void Header_constants_match_format_spec()
    {
        Assert.Equal(43, Telemetry.Packet.PacketHeader.Size);
        Assert.Equal(75, Telemetry.Packet.PacketHeader.MinPacketLength);
        var h = new Telemetry.Packet.PacketHeader(1, 0, new byte[16], new byte[12], 32);
        Assert.Equal(32u, h.CiphertextLength);
        var p = new Telemetry.Packet.Payload(Telemetry.Packet.PayloadFlags.NoCompression, [], [], null);
        Assert.Equal(Telemetry.Packet.PayloadFlags.NoCompression, p.Flags);
    }

    [Fact]
    public void Q_result_holds_components()
    {
        var q = new Telemetry.System.QResult(0.5, 1, 1, 0.75);
        Assert.Equal(0.75, q.Q);
        Assert.Equal(0.5, q.S);
    }

    [Fact]
    public void Params_and_generation_record_are_values()
    {
        Assert.Equal(new Params(64, 7.0, 1.0), new Params(64, 7.0, 1.0));
        Assert.Equal(1.0, new GenerationRecord(0, 1.0, 0.5).Best);
        var stats = new ChannelStats(10, 1, 2, 0.1, 0.25, 1.5, 16.9);
        Assert.Equal(2, stats.OverBudget);
    }
}
