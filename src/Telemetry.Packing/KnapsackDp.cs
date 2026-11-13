using System.Diagnostics.CodeAnalysis;
using Telemetry.Core;

namespace Telemetry.Packing;

/// <summary>
/// Рюкзак 0/1 динамічним програмуванням, O(n·W), з відновленням розв'язку.
/// Ваги — довжини 12 байтів кадру за ЄДИНОЮ таблицею Гаффмана вікна (64 кадри);
/// W = 8·(max_payload − 14 − 2·table_nsym), CLASS_TABLE = 0. Власник: Георгій.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Заглушка каркаса; прибрати атрибут разом з реалізацією")]
public sealed class KnapsackDp : IPacker
{
    /// <inheritdoc/>
    public int[] Select(int[] weightsBits, int[] values, int capacityBits) => throw new NotImplementedException();
}
