using System.Diagnostics.CodeAnalysis;
using Telemetry.Core;

namespace Telemetry.Huffman;

/// <summary>
/// Канонічний Гаффман (FORMAT_SPEC §3.2–3.3): дерево через PriorityQueue, length-limited до 15 біт
/// (package-merge або згладжування частот; зливати символи ЗАБОРОНЕНО), канонічні коди
/// за зростанням символу. Власник: Саша.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Заглушка каркаса; прибрати атрибут разом з реалізацією")]
public sealed class HuffmanCodec : IHuffmanCodec
{
    /// <summary>Максимальна довжина коду.</summary>
    public const int MaxCodeLength = 15;

    /// <inheritdoc/>
    public byte[] BuildLengths(long[] freq256) => throw new NotImplementedException();

    /// <inheritdoc/>
    public (byte[] Data, int Bits) Encode(byte[] src, byte[] lengths) => throw new NotImplementedException();

    /// <inheritdoc/>
    public byte[] Decode(byte[] data, byte[] lengths, int symbolCount) => throw new NotImplementedException();

    /// <summary>Канонічні коди за алгоритмом FORMAT_SPEC §3.3 (індекс = символ).</summary>
    public static uint[] CanonicalCodes(byte[] lengths) => throw new NotImplementedException();
}
