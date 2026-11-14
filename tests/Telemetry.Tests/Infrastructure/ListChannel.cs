using Telemetry.Core;

namespace Telemetry.Tests.Infrastructure;

/// <summary>Канал без втрат і бюджету: збирає всі пакети по порядку (для модульних тестів системи).</summary>
internal sealed class ListChannel : IChannel
{
    private readonly List<byte[]> _packets = [];

    public IReadOnlyList<byte[]> Packets => _packets;

    public (bool Delivered, double? Arrival) Send(byte[] packet)
    {
        _packets.Add(packet);
        return (true, _packets.Count);
    }

    public ChannelStats Stats() => new(_packets.Count, 0, 0, 0, 0, 0, double.PositiveInfinity);
}
