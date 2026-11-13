using Telemetry.Core;
using Telemetry.Huffman;
using Telemetry.Packet;

namespace Telemetry.System;

/// <summary>
/// Система «передавач + приймач» для одного прогону Runner. Runner залежить лише від цього інтерфейсу,
/// тож модулі підключаються в одному місці (<see cref="TelemetrySystem"/>), а тести підставляють свою систему.
/// Власник: тімлід.
/// </summary>
public interface ISessionSystem
{
    /// <summary>Передавач: кадри → пакети → планувальник → seq → шифрування → канал.</summary>
    /// <param name="frames">Кадри сеансу.</param>
    /// <param name="channel">Канал сценарію.</param>
    /// <param name="budgetSeconds">Бюджет часу передавання сценарію, с.</param>
    void Transmit(Frame[] frames, IChannel channel, double budgetSeconds);

    /// <summary>Приймач: доставлені пакети в порядку прибуття → відновлені кадри за timestamp_ms.</summary>
    /// <param name="packetsInArrivalOrder">Пакети, які канал доставив.</param>
    IReadOnlyList<Frame> Receive(IEnumerable<byte[]> packetsInArrivalOrder);
}

/// <summary>
/// Справжня система команди: <see cref="Sender"/> + <see cref="IScheduler"/> + <see cref="Receiver"/>.
/// Поки модулі зон — заглушки, прогін завершується обробленим <see cref="NotImplementedException"/>,
/// який Runner записує в журнал. Після інтеграції зон цей клас змінювати не потрібно, якщо їхні сигнатури
/// лишаються як у каркасі.
/// </summary>
/// <param name="password">Пароль сеансу (лише для приймача; у журнал не потрапляє).</param>
/// <param name="scheduler">Планувальник черги відправлення.</param>
public sealed class TelemetrySystem(string password, IScheduler scheduler) : ISessionSystem
{
    /// <inheritdoc/>
    public void Transmit(Frame[] frames, IChannel channel, double budgetSeconds)
    {
        ArgumentNullException.ThrowIfNull(frames);
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(scheduler);
        var sender = new Sender();
        var packets = sender.BuildPackets(frames);
        var ordered = scheduler.Order(packets, budgetSeconds);
        sender.Transmit(ordered, channel);
    }

    /// <inheritdoc/>
    public IReadOnlyList<Frame> Receive(IEnumerable<byte[]> packetsInArrivalOrder)
    {
        ArgumentNullException.ThrowIfNull(packetsInArrivalOrder);
        var receiver = new Receiver(password, new HuffmanCodec());
        foreach (var packet in packetsInArrivalOrder)
        {
            receiver.Accept(packet);
        }

        return receiver.Frames;
    }
}
