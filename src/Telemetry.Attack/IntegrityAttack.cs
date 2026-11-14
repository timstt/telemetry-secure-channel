using Telemetry.Core;

namespace Telemetry.Attack;

/// <summary>Підсумок атаки на цілісність.</summary>
/// <param name="Attempts">Скільки модифікованих пакетів подано приймачу.</param>
/// <param name="Accepted">Скільки з них приймач прийняв (успішна атака = помилка захисту).</param>
/// <param name="Exceptions">Скільки разів приймач кинув виняток (теж дефект: має повертати код відмови).</param>
/// <param name="ByError">Розподіл кодів відмови.</param>
public sealed record IntegrityReport(int Attempts, int Accepted, int Exceptions, IReadOnlyDictionary<PacketError, int> ByError)
{
    /// <summary>Захист витримав: жодної прийнятої модифікації і жодного винятку.</summary>
    public bool Passed => Accepted == 0 && Exceptions == 0;
}

/// <summary>
/// Базова перевірка цілісності (PROJECT_1 §3.7 п.3): атакувальник змінює один або кілька байтів перехопленого
/// пакета, обрізає, дописує або переставляє байти і дивиться на реакцію приймача. Приймач задається функцією,
/// тож так само перевіряється приймач іншої команди (крос-тести тижня 12).
/// </summary>
public static class IntegrityAttack
{
    /// <summary>Усі модифікації пакета: інверсія кожного байта, зміна кожного біта в заголовку, випадкові
    /// багатобайтові зміни, усі обрізання, дописаний байт, обмін половин шифротексту.</summary>
    public static IEnumerable<byte[]> Mutations(byte[] packet, int randomMulti = 200, int seed = 1)
    {
        ArgumentNullException.ThrowIfNull(packet);
        for (var i = 0; i < packet.Length; i++)
        {
            var copy = (byte[])packet.Clone();
            copy[i] = (byte)~copy[i];
            yield return copy;
        }

        for (var i = 0; i < Math.Min(43, packet.Length); i++)
        {
            for (var bit = 0; bit < 8; bit++)
            {
                var copy = (byte[])packet.Clone();
                copy[i] ^= (byte)(1 << bit);
                yield return copy;
            }
        }

#pragma warning disable CA5394 // не криптографічне використання: генерація тестових модифікацій
        var rng = new Random(seed);
        for (var n = 0; n < randomMulti && packet.Length > 0; n++)
        {
            var copy = (byte[])packet.Clone();
            var changes = rng.Next(2, 6);
            for (var k = 0; k < changes; k++)
            {
                copy[rng.Next(copy.Length)] ^= (byte)rng.Next(1, 256);
            }

            yield return copy;
        }
#pragma warning restore CA5394

        for (var len = 0; len < packet.Length; len++)
        {
            yield return packet[..len];
        }

        yield return [.. packet, 0];

        if (packet.Length > 75 + 2)
        {
            var copy = (byte[])packet.Clone();
            var body = copy.AsSpan(43, packet.Length - 75);
            var half = body.Length / 2;
            var first = body[..half].ToArray();
            body[(body.Length - half)..].CopyTo(body);
            first.CopyTo(body[(body.Length - half)..]);
            if (!copy.AsSpan().SequenceEqual(packet))
            {
                yield return copy;
            }
        }
    }

    /// <summary>Подає всі модифікації кожного пакета приймачу і рахує результат.</summary>
    /// <param name="packets">Перехоплені справжні пакети.</param>
    /// <param name="newReceiver">Новий приймач на кожну спробу (щоб дедуплікація seq не маскувала атаку).</param>
    public static IntegrityReport Run(IEnumerable<byte[]> packets, Func<Func<byte[], OpenResult>> newReceiver)
    {
        ArgumentNullException.ThrowIfNull(packets);
        ArgumentNullException.ThrowIfNull(newReceiver);
        int attempts = 0, accepted = 0, exceptions = 0;
        var byError = new Dictionary<PacketError, int>();
        foreach (var packet in packets)
        {
            foreach (var modified in Mutations(packet))
            {
                attempts++;
                try
                {
                    var r = newReceiver()(modified);
                    byError[r.Error] = byError.GetValueOrDefault(r.Error) + 1;
                    if (r.Ok)
                    {
                        accepted++;
                    }
                }
                catch (Exception)
                {
                    exceptions++;
                }
            }
        }

        return new IntegrityReport(attempts, accepted, exceptions, byError);
    }
}
