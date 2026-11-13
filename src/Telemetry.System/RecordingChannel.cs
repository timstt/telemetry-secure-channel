using System.Diagnostics;
using Telemetry.Core;

namespace Telemetry.System;

/// <summary>Пакет, який канал доставив.</summary>
/// <param name="Bytes">Копія байтів пакета.</param>
/// <param name="Arrival">Час прибуття за годинником каналу, с.</param>
/// <param name="SendOrder">Порядковий номер відправлення (розв'язує рівні часи прибуття).</param>
public sealed record DeliveredPacket(byte[] Bytes, double Arrival, int SendOrder);

/// <summary>
/// Обгортка каналу для Runner: рахує B_sent, збирає доставлені пакети для приймача і міряє час,
/// проведений у виклику каналу (він не входить у час обчислень t метрики Q). Власник: тімлід.
/// </summary>
/// <remarks>
/// Бюджет моделюється рівно як у <c>channel.py</c>: <c>need = bits / bandwidth</c>; якщо
/// <c>clock + need &gt; budget</c>, пакет не передається і в B_sent не входить. Інакше він входить у B_sent
/// навіть тоді, коли канал його втратив (README, «Метрика Q»). Арифметика — IEEE double, як у Python,
/// тому рішення збігаються; Runner звіряє лічильник із <c>stats()["надіслано"]</c>.
/// </remarks>
public sealed class RecordingChannel : IChannel
{
    private readonly IChannel _inner;
    private readonly double _bandwidthBps;
    private readonly double _budgetSeconds;
    private readonly List<DeliveredPacket> _delivered = [];
    private readonly Stopwatch _channelTime = new();
    private double _clockSeconds;

    /// <summary>Обгортає канал сценарію.</summary>
    /// <param name="inner">Справжній канал (зазвичай <see cref="ChannelBridge"/>).</param>
    /// <param name="bandwidthBps">bandwidth_bps сценарію.</param>
    /// <param name="budgetSeconds">transmission_budget_s сценарію (∞, якщо не задано).</param>
    public RecordingChannel(IChannel inner, double bandwidthBps, double budgetSeconds)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bandwidthBps);
        if (double.IsNaN(budgetSeconds) || budgetSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(budgetSeconds), budgetSeconds, "бюджет має бути ≥ 0");
        }

        _inner = inner;
        _bandwidthBps = bandwidthBps;
        _budgetSeconds = budgetSeconds;
    }

    /// <summary>B_sent: біти пакетів, відданих у канал (включно з утраченими, без невміщених у бюджет).</summary>
    public long SentBits { get; private set; }

    /// <summary>Скільки разів викликано <see cref="Send"/>.</summary>
    public int Attempted { get; private set; }

    /// <summary>Пакети, що вмістилися в бюджет (= stats()["надіслано"]).</summary>
    public int Transmitted { get; private set; }

    /// <summary>Пакети, що не вмістилися в бюджет.</summary>
    public int OverBudget { get; private set; }

    /// <summary>Доставлені пакети в порядку відправлення.</summary>
    public IReadOnlyList<DeliveredPacket> Delivered => _delivered;

    /// <summary>Сумарний час усередині викликів каналу (міст до Python), не час обчислень системи.</summary>
    public TimeSpan ChannelCallTime => _channelTime.Elapsed;

    /// <inheritdoc/>
    public (bool Delivered, double? Arrival) Send(byte[] packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        var bits = 8L * packet.Length;
        var need = bits / _bandwidthBps;
        var fits = !(_clockSeconds + need > _budgetSeconds);

        _channelTime.Start();
        (bool Delivered, double? Arrival) result;
        try
        {
            result = _inner.Send(packet);
        }
        finally
        {
            _channelTime.Stop();
        }

        Attempted++;
        if (fits)
        {
            _clockSeconds += need;
            SentBits += bits;
            Transmitted++;
        }
        else
        {
            OverBudget++;
        }

        if (result.Delivered && result.Arrival is { } arrival)
        {
            _delivered.Add(new DeliveredPacket([.. packet], arrival, _delivered.Count));
        }

        return result;
    }

    /// <inheritdoc/>
    public ChannelStats Stats()
    {
        _channelTime.Start();
        try
        {
            return _inner.Stats();
        }
        finally
        {
            _channelTime.Stop();
        }
    }

    /// <summary>Доставлені пакети в порядку прибуття (рівні часи — за порядком відправлення).</summary>
    public IEnumerable<byte[]> DeliveredInArrivalOrder() =>
        _delivered.OrderBy(p => p.Arrival).ThenBy(p => p.SendOrder).Select(p => p.Bytes);
}
