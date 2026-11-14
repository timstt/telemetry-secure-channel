using Telemetry.Core;
using Telemetry.Huffman;
using Telemetry.Packet;
using Telemetry.Packing;
using Telemetry.Predict;
using Telemetry.Security;

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

/// <summary>Блоки системи, які можна вимкнути для таблиці 2 (вплив кожного блоку).</summary>
/// <param name="Sender">Налаштування передавача (стиснення, рюкзак, дублювання, гени ГА).</param>
/// <param name="UseClassifier">false — усім кадрам клас 0 (вимкнений «Прогноз»).</param>
/// <param name="UseDpPacker">false — жадібний рюкзак замість ДП.</param>
public sealed record SystemOptions(SenderOptions? Sender = null, bool UseClassifier = true, bool UseDpPacker = true);

/// <summary>
/// Справжня система команди: <see cref="Sender"/> + <see cref="IScheduler"/> + <see cref="Receiver"/>.
/// Один кеш KDF на прогін: PBKDF2 виконується один раз на сеанс (FORMAT_SPEC §4.1).
/// </summary>
/// <param name="config">team_config.json (пароль лише для криптографії; у журнал не потрапляє).</param>
/// <param name="scheduler">Планувальник черги відправлення.</param>
/// <param name="options">Вмикання і вимикання блоків.</param>
public sealed class TelemetrySystem(TeamConfig config, IScheduler scheduler, SystemOptions? options = null) : ISessionSystem
{
    private readonly SystemOptions options = options ?? new SystemOptions();
    private readonly Kdf kdf = new();

    /// <summary>Пакети останнього Transmit (до планувальника) — для звітів.</summary>
    public IReadOnlyList<PacketPlan> LastPlans { get; private set; } = [];

    /// <inheritdoc/>
    public void Transmit(Frame[] frames, IChannel channel, double budgetSeconds)
    {
        ArgumentNullException.ThrowIfNull(frames);
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(scheduler);
        var sender = new Sender(
            config,
            new SessionCrypto(config.SessionPassword, kdf),
            options.UseClassifier ? new Classifier() : new ConstantClassifier(),
            options.UseDpPacker ? new KnapsackDp() : new KnapsackGreedy(),
            options.Sender);
        var packets = sender.BuildPackets(frames);
        LastPlans = packets;
        var ordered = scheduler.Order(packets, budgetSeconds);
        sender.Transmit(ordered, channel);
    }

    /// <inheritdoc/>
    public IReadOnlyList<Frame> Receive(IEnumerable<byte[]> packetsInArrivalOrder)
    {
        ArgumentNullException.ThrowIfNull(packetsInArrivalOrder);
        // Власне розширення DUPLICATION приймач розуміє завжди: дублі зараховуються один раз.
        const PayloadFlags allowed = PayloadFlags.Duplication;
        var receiver = new Receiver(new SessionCrypto(config.SessionPassword, kdf), new HuffmanCodec(), allowed);
        foreach (var packet in packetsInArrivalOrder)
        {
            receiver.Accept(packet);
        }

        return receiver.Frames;
    }
}
