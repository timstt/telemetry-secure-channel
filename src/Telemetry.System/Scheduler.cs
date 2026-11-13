using Telemetry.Core;

namespace Telemetry.System;

/// <summary>
/// Жадібний планувальник черги відправлення. Власник: тімлід.
/// </summary>
/// <remarks>
/// <para>
/// Задача: пакети k = 1..n з цінністю V_k і часом передавання t_k = b_k / bandwidth. Канал передає їх у
/// заданому порядку і ПРОПУСКАЄ пакет, що не вміщується в залишок бюджету (наступні, менші, ще можуть
/// пройти — див. channel.py). Отже, порядок відправлення = порядок, у якому жадібний рюкзак 0/1
/// (вартість — час) пробує предмети: «бери, якщо влазить, інакше пропусти».
/// </para>
/// <para>
/// Правило: сортування за щільністю V_k / t_k за спаданням; рівні — більший V_k, далі менший Index.
/// Оскільки bandwidth однакова для всіх пакетів сеансу, V_k / t_k ∝ V_k / b_k, тому планувальнику
/// не потрібна смуга. Щільності порівнюються точно, у цілих числах: V_a·b_b проти V_b·b_a
/// (без похибок double, тому порядок детермінований). Складність O(n log n).
/// </para>
/// <para>
/// Порядок не залежить від seed каналу і від порядку вхідного масиву (повний строгий порядок через Index).
/// Бюджет у порядку не використовується: пакети, що не влізуть, канал пропустить сам, а менші після них
/// ще пройдуть.
/// </para>
/// </remarks>
public sealed class Scheduler : IScheduler
{
    /// <inheritdoc/>
    public PacketPlan[] Order(PacketPlan[] packets, double budgetSeconds)
    {
        ArgumentNullException.ThrowIfNull(packets);
        var ordered = (PacketPlan[])packets.Clone();
        Array.Sort(ordered, CompareByDensity);
        return ordered;
    }

    /// <summary>
    /// Порівняння «a раніше за b»: більша щільність V/b, далі більша V, далі менший Index.
    /// </summary>
    public static int CompareByDensity(PacketPlan? a, PacketPlan? b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);

        // V_a / b_a > V_b / b_b  ⇔  V_a·b_b > V_b·b_a  (усі розміри > 0, бо містять 75 Б заголовка й HMAC).
        var left = (long)a.TotalValue * b.SizeBits;
        var right = (long)b.TotalValue * a.SizeBits;
        if (left != right)
        {
            return right.CompareTo(left); // спадання щільності
        }

        if (a.TotalValue != b.TotalValue)
        {
            return b.TotalValue.CompareTo(a.TotalValue); // більша цінність раніше
        }

        return a.Index.CompareTo(b.Index); // стабільний детермінований розв'язок рівностей
    }

    /// <summary>
    /// Варіант «найкращий із двох» (для захисту, ≥ ½ OPT): жадібний набір, що влазить у бюджет, проти
    /// найціннішого одного пакета, що влазить. Повертає кращий з двох наборів у порядку відправлення.
    /// </summary>
    /// <param name="packets">Пакети.</param>
    /// <param name="budgetSeconds">Бюджет часу передавання, с.</param>
    /// <param name="bandwidthBps">Смуга каналу, біт/с.</param>
    public static PacketPlan[] BestOfTwo(PacketPlan[] packets, double budgetSeconds, double bandwidthBps)
    {
        ArgumentNullException.ThrowIfNull(packets);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bandwidthBps);

        var greedy = new List<PacketPlan>();
        var used = 0.0;
        var greedyValue = 0L;
        foreach (var p in new Scheduler().Order(packets, budgetSeconds))
        {
            var t = p.SizeBits / bandwidthBps;
            if (used + t <= budgetSeconds)
            {
                greedy.Add(p);
                used += t;
                greedyValue += p.TotalValue;
            }
        }

        var best = packets
            .Where(p => p.SizeBits / bandwidthBps <= budgetSeconds)
            .OrderByDescending(p => p.TotalValue)
            .ThenBy(p => p.Index)
            .FirstOrDefault();

        return best is not null && best.TotalValue > greedyValue ? [best] : [.. greedy];
    }
}

/// <summary>Базовий порядок «за часом створення» (вимкнений планувальник). Для таблиці 4.</summary>
public sealed class FifoScheduler : IScheduler
{
    /// <inheritdoc/>
    public PacketPlan[] Order(PacketPlan[] packets, double budgetSeconds)
    {
        ArgumentNullException.ThrowIfNull(packets);
        return [.. packets.OrderBy(p => p.Index)];
    }
}

/// <summary>Базовий випадковий порядок (Фішер — Єйтс з фіксованим seed). Для таблиці 4.</summary>
/// <param name="seed">Seed перемішування (не seed каналу).</param>
public sealed class RandomScheduler(int seed) : IScheduler
{
    /// <inheritdoc/>
    public PacketPlan[] Order(PacketPlan[] packets, double budgetSeconds)
    {
        ArgumentNullException.ThrowIfNull(packets);
        var result = packets.OrderBy(p => p.Index).ToArray();
#pragma warning disable CA5394 // не криптографічне використання: базова лінія експерименту
        var rng = new Random(seed);
        for (var i = result.Length - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (result[i], result[j]) = (result[j], result[i]);
        }
#pragma warning restore CA5394

        return result;
    }
}
