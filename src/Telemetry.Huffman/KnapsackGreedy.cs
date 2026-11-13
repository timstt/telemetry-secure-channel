using System.Diagnostics.CodeAnalysis;
using Telemetry.Core;

namespace Telemetry.Huffman;

/// <summary>Жадібний рюкзак: сортування за v/w за спаданням. Порівнюється з ДП (таблиця 1). Власник: Саша.</summary>
[ExcludeFromCodeCoverage(Justification = "Заглушка каркаса; прибрати атрибут разом з реалізацією")]
public sealed class KnapsackGreedy : IPacker
{
    /// <inheritdoc/>
    public int[] Select(int[] weightsBits, int[] values, int capacityBits) => throw new NotImplementedException();
}
