using Telemetry.Core;

namespace Telemetry.Huffman;

/// <summary>
/// Канонічний Гаффман (FORMAT_SPEC §3.2–3.3). Довжини кодів — жадібне дерево через PriorityQueue;
/// якщо вони перевищують 15 біт, довжини перераховуються package-merge (символи не зливаються).
/// </summary>
public sealed class HuffmanCodec : IHuffmanCodec
{
    /// <summary>Максимальна довжина коду.</summary>
    public const int MaxCodeLength = 15;

    private const int AlphabetSize = 256;

    /// <inheritdoc/>
    public byte[] BuildLengths(long[] freq256)
    {
        CheckFrequencies(freq256);

        var lengths = new byte[AlphabetSize];
        var symbols = Enumerable.Range(0, AlphabetSize).Where(s => freq256[s] > 0).ToArray();
        if (symbols.Length == 0)
        {
            return lengths;
        }

        if (symbols.Length == 1)
        {
            lengths[symbols[0]] = 1;
            return lengths;
        }

        var depths = TreeDepths(freq256, symbols);
        if (depths.Max() > MaxCodeLength)
        {
            return BuildLengthsLimited(freq256, MaxCodeLength);
        }

        for (var i = 0; i < symbols.Length; i++)
        {
            lengths[symbols[i]] = (byte)depths[i];
        }

        return lengths;
    }

    /// <summary>
    /// Оптимальні довжини з обмеженням <paramref name="maxLength"/> (package-merge).
    /// Потрібно, щоб кількість символів не перевищувала 2^maxLength.
    /// </summary>
    public static byte[] BuildLengthsLimited(long[] freq256, int maxLength)
    {
        CheckFrequencies(freq256);
        var lengths = new byte[AlphabetSize];
        var symbols = Enumerable.Range(0, AlphabetSize)
            .Where(s => freq256[s] > 0)
            .OrderBy(s => freq256[s])
            .ThenBy(s => s)
            .ToArray();

        if (symbols.Length == 0)
        {
            return lengths;
        }

        if (symbols.Length == 1)
        {
            lengths[symbols[0]] = 1;
            return lengths;
        }

        if (symbols.Length > 1L << maxLength)
        {
            throw new ArgumentException("Забагато символів для заданого обмеження довжини.");
        }

        var leaves = symbols.Select(s => new Item(freq256[s], s, null, null)).ToList();
        var level = leaves;
        for (var d = maxLength - 1; d >= 1; d--)
        {
            var packages = new List<Item>();
            for (var i = 0; i + 1 < level.Count; i += 2)
            {
                packages.Add(new Item(level[i].Weight + level[i + 1].Weight, -1, level[i], level[i + 1]));
            }

            level = MergeSorted(leaves, packages);
        }

        var stack = new Stack<Item>(level.Take(2 * symbols.Length - 2));
        while (stack.Count > 0)
        {
            var item = stack.Pop();
            if (item.Left is null)
            {
                lengths[item.Symbol]++;
            }
            else
            {
                stack.Push(item.Left);
                stack.Push(item.Right!);
            }
        }

        return lengths;
    }

    /// <inheritdoc/>
    public (byte[] Data, int Bits) Encode(byte[] src, byte[] lengths)
    {
        ArgumentNullException.ThrowIfNull(src);
        var codes = CanonicalCodes(lengths);

        var writer = new BitWriter();
        foreach (var b in src)
        {
            if (lengths[b] == 0)
            {
                throw new InvalidDataException($"Символ {b} відсутній у таблиці кодів.");
            }

            writer.Write(codes[b], lengths[b]);
        }

        return (writer.ToArray(), writer.BitCount);
    }

    /// <inheritdoc/>
    public byte[] Decode(byte[] data, byte[] lengths, int symbolCount)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentOutOfRangeException.ThrowIfNegative(symbolCount);
        CheckLengths(lengths);

        var count = new int[MaxCodeLength + 1];
        foreach (var l in lengths)
        {
            count[l]++;
        }

        count[0] = 0;
        var firstCode = new int[MaxCodeLength + 1];
        var firstIndex = new int[MaxCodeLength + 1];
        var code = 0;
        var index = 0;
        for (var l = 1; l <= MaxCodeLength; l++)
        {
            code = (code + count[l - 1]) << 1;
            firstCode[l] = code;
            firstIndex[l] = index;
            index += count[l];
        }

        var sorted = new byte[index];
        var fill = (int[])firstIndex.Clone();
        for (var s = 0; s < AlphabetSize; s++)
        {
            if (lengths[s] != 0)
            {
                sorted[fill[lengths[s]]++] = (byte)s;
            }
        }

        var reader = new BitReader(data);
        var result = new byte[symbolCount];
        for (var n = 0; n < symbolCount; n++)
        {
            var current = 0;
            var found = false;
            for (var l = 1; l <= MaxCodeLength && !found; l++)
            {
                if (!reader.TryReadBit(out var bit))
                {
                    throw new InvalidDataException("Бітовий потік обірвано.");
                }

                current = (current << 1) | bit;
                var offset = current - firstCode[l];
                if (offset >= 0 && offset < count[l])
                {
                    result[n] = sorted[firstIndex[l] + offset];
                    found = true;
                }
            }

            if (!found)
            {
                throw new InvalidDataException("Послідовність бітів не є кодом жодного символу.");
            }
        }

        return result;
    }

    /// <summary>Канонічні коди за алгоритмом FORMAT_SPEC §3.3 (індекс = символ).</summary>
    public static uint[] CanonicalCodes(byte[] lengths)
    {
        CheckLengths(lengths);

        var blCount = new int[MaxCodeLength + 1];
        foreach (var l in lengths)
        {
            blCount[l]++;
        }

        blCount[0] = 0;
        var nextCode = new uint[MaxCodeLength + 1];
        uint code = 0;
        for (var l = 1; l <= MaxCodeLength; l++)
        {
            code = (code + (uint)blCount[l - 1]) << 1;
            nextCode[l] = code;
        }

        var codes = new uint[AlphabetSize];
        for (var s = 0; s < AlphabetSize; s++)
        {
            if (lengths[s] != 0)
            {
                codes[s] = nextCode[lengths[s]]++;
            }
        }

        return codes;
    }

    private static int[] TreeDepths(long[] freq, int[] symbols)
    {
        // Рівні частоти: листки за значенням символу, внутрішні вузли за порядком створення.
        var queue = new PriorityQueue<Node, (long Weight, int Order)>();
        var leaves = new Node[symbols.Length];
        for (var i = 0; i < symbols.Length; i++)
        {
            leaves[i] = new Node(freq[symbols[i]], i, null, null);
            queue.Enqueue(leaves[i], (leaves[i].Weight, symbols[i]));
        }

        var created = AlphabetSize;
        while (queue.Count > 1)
        {
            var a = queue.Dequeue();
            var b = queue.Dequeue();
            var parent = new Node(a.Weight + b.Weight, -1, a, b);
            queue.Enqueue(parent, (parent.Weight, created++));
        }

        var depths = new int[symbols.Length];
        var stack = new Stack<(Node Node, int Depth)>();
        stack.Push((queue.Dequeue(), 0));
        while (stack.Count > 0)
        {
            var (node, depth) = stack.Pop();
            if (node.Left is null)
            {
                depths[node.Index] = depth;
            }
            else
            {
                stack.Push((node.Left, depth + 1));
                stack.Push((node.Right!, depth + 1));
            }
        }

        return depths;
    }

    private static List<Item> MergeSorted(List<Item> leaves, List<Item> packages)
    {
        var merged = new List<Item>(leaves.Count + packages.Count);
        int i = 0, j = 0;
        while (i < leaves.Count || j < packages.Count)
        {
            if (j == packages.Count || (i < leaves.Count && leaves[i].Weight <= packages[j].Weight))
            {
                merged.Add(leaves[i++]);
            }
            else
            {
                merged.Add(packages[j++]);
            }
        }

        return merged;
    }

    private static void CheckFrequencies(long[] freq)
    {
        ArgumentNullException.ThrowIfNull(freq);
        if (freq.Length != AlphabetSize)
        {
            throw new ArgumentException("Очікується 256 частот.", nameof(freq));
        }

        if (freq.Any(f => f < 0))
        {
            throw new ArgumentException("Частота не може бути від'ємною.", nameof(freq));
        }
    }

    private static void CheckLengths(byte[] lengths)
    {
        ArgumentNullException.ThrowIfNull(lengths);
        if (lengths.Length != AlphabetSize)
        {
            throw new ArgumentException("Очікується 256 довжин.", nameof(lengths));
        }

        long kraft = 0;
        foreach (var l in lengths)
        {
            if (l > MaxCodeLength)
            {
                throw new InvalidDataException($"Довжина коду {l} більша за {MaxCodeLength}.");
            }

            if (l != 0)
            {
                kraft += 1L << (MaxCodeLength - l);
            }
        }

        if (kraft > 1L << MaxCodeLength)
        {
            throw new InvalidDataException("Довжини порушують нерівність Крафта.");
        }
    }

    private sealed record Node(long Weight, int Index, Node? Left, Node? Right);

    private sealed record Item(long Weight, int Symbol, Item? Left, Item? Right);
}
