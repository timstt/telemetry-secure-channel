using System.Diagnostics.CodeAnalysis;
using Telemetry.Core;

namespace Telemetry.System;

/// <summary>
/// Передавач: класифікація → черга за цінністю (пастка 18) → вікно 64 → таблиця Гаффмана → ДП-вибір →
/// вибір режиму Гаффман/NO_COMPRESSION (пастка 19) → пакет → планувальник → seq після планувальника →
/// шифрування → канал. Власник: тімлід.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Заглушка каркаса; прибрати атрибут разом з реалізацією")]
public sealed class Sender
{
    /// <summary>Формує пакети сеансу (до присвоєння seq і шифрування).</summary>
    public PacketPlan[] BuildPackets(Frame[] frames) => throw new NotImplementedException();

    /// <summary>Присвоює seq у порядку відправлення, шифрує і віддає в канал.</summary>
    public void Transmit(PacketPlan[] ordered, IChannel channel) => throw new NotImplementedException();
}
