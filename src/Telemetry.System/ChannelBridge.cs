using System.Diagnostics.CodeAnalysis;
using Telemetry.Core;

namespace Telemetry.System;

/// <summary>
/// Міст до НЕЗМІНЕНОГО channel/channel.py: один дочірній процес python3 channel/bridge.py на прогін,
/// протокол — рядок JSON у stdin/stdout ({"init"}, {"send"}, {"stats"}). Не переписувати канал на C#:
/// послідовність втрат залежить від random.Random Python. Власник: тімлід.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Заглушка каркаса; прибрати атрибут разом з реалізацією")]
public sealed class ChannelBridge : IChannel, IDisposable
{
    /// <summary>Запускає міст для сценарію (сирий JSON з team_config) і seed.</summary>
    public ChannelBridge(string scenarioJson, int seed) => throw new NotImplementedException();

    /// <inheritdoc/>
    public (bool Delivered, double? Arrival) Send(byte[] packet) => throw new NotImplementedException();

    /// <inheritdoc/>
    public ChannelStats Stats() => throw new NotImplementedException();

    /// <inheritdoc/>
    public void Dispose() => throw new NotImplementedException();
}
