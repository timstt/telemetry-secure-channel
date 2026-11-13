using System.Diagnostics.CodeAnalysis;
using Telemetry.Core;

namespace Telemetry.Packet;

/// <summary>
/// Приймач. Порядок (FORMAT_SPEC §5): довжина ≥ 75 → magic/version → довжина vs ciphertext_len →
/// HMAC у постійний час → лише потім розшифрування → reserved/flags → декодування.
/// Розмір &gt; max_packet_bytes НЕ є причиною відмови. Збирає кадри з усіх пакетів, ігнорує дублікати
/// seq, сортує кадри за timestamp_ms. Власник: Коля.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Заглушка каркаса; прибрати атрибут разом з реалізацією")]
public sealed class Receiver
{
    /// <summary>Створює приймач для пароля сеансу.</summary>
    public Receiver(string password, IHuffmanCodec codec) => throw new NotImplementedException();

    /// <summary>Обробляє один пакет; ніколи не кидає винятків назовні.</summary>
    public OpenResult Accept(byte[] packet) => throw new NotImplementedException();

    /// <summary>Усі відновлені кадри, впорядковані за timestamp_ms.</summary>
    public IReadOnlyList<Frame> Frames => throw new NotImplementedException();
}
