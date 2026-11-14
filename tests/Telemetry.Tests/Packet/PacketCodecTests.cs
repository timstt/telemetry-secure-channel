using System.Buffers.Binary;
using Telemetry.Core;
using Telemetry.Huffman;
using Telemetry.Packet;
using Telemetry.Security;
using Telemetry.Tests.Infrastructure;

namespace Telemetry.Tests.Packet;

/// <summary>PacketCodec і Receiver (зона Колі): формат §2–3, коди §6, стійкість C5 п.5.</summary>
public class PacketCodecTests
{
    private const string Password = "algo2026";
    private static readonly Kdf SharedKdf = new();
    private static readonly HuffmanCodec Codec = new();

    private static readonly string[] References =
        ["ref_01_single_frame", "ref_02_empty", "ref_03_degenerate", "ref_04_session", "ref_05_no_compression", "ref_06_class_table"];

    private static byte[] Plaintext(string name) =>
        new SessionCrypto(Password, SharedKdf).Open(File.ReadAllBytes(TestData.Reference(name + ".bin"))).Payload!;

    private static Receiver NewReceiver(PayloadFlags allowed = PayloadFlags.None) =>
        new(new SessionCrypto(Password, SharedKdf), Codec, allowed);

    private static byte[] Seal(uint seq, byte[] payload)
    {
        var nonce = TestData.Hex("010203040506070800000000");
        BinaryPrimitives.WriteUInt32BigEndian(nonce.AsSpan(8), seq);
        var header = PacketCodec.WriteHeader(new PacketHeader(
            1, seq, TestData.Hex("000102030405060708090A0B0C0D0E0F"), nonce, (uint)payload.Length));
        return new SessionCrypto(Password, SharedKdf).Seal(header, payload);
    }

    [Fact]
    public void Reference_payloads_reencode_byte_for_byte()
    {
        foreach (var name in References)
        {
            var plaintext = Plaintext(name);
            var payload = PacketCodec.ReadPayload(plaintext, Codec);
            Assert.Equal(plaintext, PacketCodec.WritePayload(payload, Codec));
            Assert.Equal(plaintext.Length, PacketCodec.PayloadSize(payload.Flags, payload.Lengths, payload.Frames));
        }
    }

    [Fact]
    public void Header_roundtrip_matches_ref01()
    {
        var packet = File.ReadAllBytes(TestData.Reference("ref_01_single_frame.bin"));
        var header = PacketCodec.ReadHeader(packet);
        Assert.Equal((ushort)1, header.DeviceId);
        Assert.Equal(0u, header.PacketSeq);
        Assert.Equal(32u, header.CiphertextLength);
        Assert.Equal(packet[..43], PacketCodec.WriteHeader(header));
        Assert.Equal(PacketError.E_SHORT, Assert.Throws<PacketFormatException>(() => PacketCodec.ReadHeader(packet.AsSpan(0, 42))).Code);
        packet[0] = 0;
        Assert.Equal(PacketError.E_MAGIC, Assert.Throws<PacketFormatException>(() => PacketCodec.ReadHeader(packet)).Code);
        Assert.Throws<ArgumentException>(() => PacketCodec.WriteHeader(header with { Salt = new byte[3] }));
        Assert.Throws<ArgumentException>(() => PacketCodec.WriteHeader(header with { Nonce = new byte[3] }));
    }

    [Fact]
    public void Table_contains_all_window_symbols_even_if_unused()
    {
        var lengths = new byte[256];
        lengths[0] = 1;
        lengths[1] = 2;
        lengths[0xAB] = 2; // символ вікна, якого немає в кадрі
        var frame = new Frame(0, 0, 0, 1, 1, 0);
        var payload = PacketCodec.WritePayload(new Payload(PayloadFlags.None, lengths, [frame], null), Codec);
        Assert.Equal(3, BinaryPrimitives.ReadUInt16BigEndian(payload.AsSpan(4)));
        Assert.Equal(payload.Length, PacketCodec.PayloadSize(PayloadFlags.None, lengths, [frame]));
        Assert.Equal([frame], PacketCodec.ReadPayload(payload, Codec).Frames);
    }

    [Fact]
    public void No_compression_and_class_table_roundtrip()
    {
        Frame[] frames = [new(1, 2, 3, 4, 5, 1), new(2, 3, 4, 5, 6, 0)];
        var flags = PayloadFlags.NoCompression | PayloadFlags.ClassTable;
        var bytes = PacketCodec.WritePayload(new Payload(flags, new byte[256], frames, [1, 2]), Codec);
        Assert.Equal(14 + 24 + 2, bytes.Length);
        Assert.Equal(bytes.Length, PacketCodec.PayloadSize(flags, new byte[256], frames));
        var back = PacketCodec.ReadPayload(bytes, Codec);
        Assert.Equal(frames, back.Frames);
        Assert.Equal(new byte[] { 1, 2 }, back.ClassTable);
    }

    [Fact]
    public void WritePayload_validates_input()
    {
        var lengths = new byte[256];
        lengths[0] = 1;
        Assert.Throws<ArgumentException>(() => PacketCodec.WritePayload(new Payload(PayloadFlags.ClassTable, lengths, [], null), Codec));
        Assert.Throws<ArgumentException>(() => PacketCodec.WritePayload(new Payload(PayloadFlags.None, lengths, [new Frame()], [0]), Codec));
        Assert.Throws<ArgumentException>(() => PacketCodec.WritePayload(new Payload(PayloadFlags.None, new byte[10], [], null), Codec));
        var tooLong = new byte[256];
        tooLong[0] = 16;
        Assert.Throws<ArgumentException>(() => PacketCodec.WritePayload(new Payload(PayloadFlags.None, tooLong, [], null), Codec));
        Assert.Throws<ArgumentException>(() => PacketCodec.WritePayload(new Payload(PayloadFlags.None, lengths, new Frame[70000], null), Codec));
    }

    public static TheoryData<string, PacketError> Corruptions() => new()
    {
        { "short", PacketError.E_PAYLOAD },
        { "flags_high", PacketError.E_PAYLOAD },
        { "nocompression_with_table", PacketError.E_PAYLOAD },
        { "unsorted_table", PacketError.E_PAYLOAD },
        { "length_zero", PacketError.E_CODE },
        { "length_16", PacketError.E_CODE },
        { "kraft", PacketError.E_CODE },
        { "symbol_count", PacketError.E_PAYLOAD },
        { "bitstream_len_too_big", PacketError.E_PAYLOAD },
        { "trailing_garbage", PacketError.E_PAYLOAD },
        { "nonzero_tail_bits", PacketError.E_CODE },
        { "truncated_stream", PacketError.E_CODE },
        { "bad_mode", PacketError.E_PAYLOAD },
        { "table_too_long", PacketError.E_PAYLOAD },
    };

    [Theory]
    [MemberData(nameof(Corruptions))]
    public void Corrupted_payloads_give_spec_codes(string kind, PacketError code)
    {
        var p = Plaintext("ref_01_single_frame"); // 0001 00 00 0007 [0001 0103 0304 D704 E804 FD04 FF03] 0000000C 00000004 0CE6DF90
        switch (kind)
        {
            case "short": p = p[..5]; break;
            case "flags_high": p[2] = 0x10; break;
            case "nocompression_with_table": p[2] = 0x08; break;
            case "unsorted_table": (p[6], p[8]) = (p[8], p[6]); break;
            case "length_zero": p[7] = 0; break;
            case "length_16": p[7] = 16; break;
            case "kraft": p[9] = 1; p[11] = 1; break;
            case "symbol_count": p[23] = 24; break;
            case "bitstream_len_too_big": p[27] = 9; break;
            case "trailing_garbage": p = [.. p, 0]; break;
            case "nonzero_tail_bits": p[^1] |= 1; break;
            case "truncated_stream": p[27] = 3; p = p[..^1]; break;
            case "bad_mode": p = BadModePayload(); break;
            case "table_too_long": p[4] = 0x0F; break;
        }

        Assert.Equal(code, Assert.Throws<PacketFormatException>(() => PacketCodec.ReadPayload(p, Codec)).Code);
    }

    private static byte[] BadModePayload()
    {
        var raw = new Frame(1, 2, 3, 4, 5, 0).ToBytes();
        raw[10] = 9;
        byte[] head = [0, 1, 0x08, 0, 0, 0, 0, 0, 0, 12, 0, 0, 0, 12];
        return [.. head, .. raw];
    }

    [Fact]
    public void Receiver_reports_payload_errors_with_seq_and_counts_them()
    {
        var p = Plaintext("ref_01_single_frame");
        p[3] = 1; // reserved
        var rx = NewReceiver();
        var r = rx.Accept(Seal(7, p));
        Assert.Equal(PacketError.E_PAYLOAD, r.Error);
        Assert.Equal(7u, r.PacketSeq);
        Assert.Null(r.Payload);
        Assert.Equal(1, rx.Rejected[PacketError.E_PAYLOAD]);
        Assert.Empty(rx.Frames);
        Assert.Equal(0, rx.Accepted);
    }

    [Fact]
    public void Receiver_survives_shuffled_duplicated_and_truncated_stream()
    {
        var rng = new Random(11);
        var all = new List<Frame>();
        var packets = new List<byte[]>();
        for (uint seq = 0; seq < 6; seq++)
        {
            var frames = Enumerable.Range(0, 5).Select(i => new Frame((uint)rng.Next(100000), (short)rng.Next(-500, 500), 200, (short)rng.Next(-50, 50), (byte)rng.Next(8), (byte)rng.Next(2))).ToArray();
            all.AddRange(frames);
            var freq = new long[256];
            foreach (var b in Frame.Serialize(frames))
            {
                freq[b]++;
            }

            packets.Add(Seal(seq, PacketCodec.WritePayload(new Payload(PayloadFlags.None, Codec.BuildLengths(freq), frames, null), Codec)));
        }

        var stream = packets.Concat(packets.Take(3)).OrderBy(_ => rng.Next()).ToList();
        stream.Add(packets[0][..50]);
        stream.Add(packets[1][..^1]);
        var rx = NewReceiver();
        foreach (var p in stream)
        {
            rx.Accept(p);
        }

        Assert.Equal(6, rx.Accepted);
        Assert.Equal(3, rx.DuplicateSeq);
        Assert.Equal(all.OrderBy(f => f.TimestampMs), rx.Frames);
        Assert.Equal(1, rx.Rejected[PacketError.E_SHORT]);
        Assert.Equal(1, rx.Rejected[PacketError.E_LENGTH]);
    }

    [Fact]
    public void Duplication_extension_counts_identical_frames_once_when_enabled()
    {
        var frame = new Frame(5, 1, 1, 1, 1, 1);
        var payload = PacketCodec.WritePayload(new Payload(PayloadFlags.NoCompression | PayloadFlags.Duplication, new byte[256], [frame], null), Codec);
        var plain = NewReceiver();
        Assert.Equal(PacketError.E_PAYLOAD, plain.Accept(Seal(0, payload)).Error);

        var dup = NewReceiver(PayloadFlags.Duplication);
        Assert.True(dup.Accept(Seal(0, payload)).Ok);
        Assert.True(dup.Accept(Seal(1, payload)).Ok);
        Assert.Equal([frame], dup.Frames);
    }

    [Fact]
    public void Receiver_never_throws_on_crypto_failure_or_random_input()
    {
        var rx = new Receiver(new ThrowingCrypto(), Codec);
        Assert.Equal(PacketError.E_PAYLOAD, rx.Accept([1, 2, 3]).Error);

        var real = NewReceiver();
        var rng = new Random(5);
        for (var i = 0; i < 200; i++)
        {
            var junk = new byte[rng.Next(0, 300)];
            rng.NextBytes(junk);
            Assert.False(real.Accept(junk).Ok);
        }
    }

    private sealed class ThrowingCrypto : ICrypto
    {
        public (byte[] KEnc, byte[] KMac) DeriveKeys(string password, byte[] salt) => throw new InvalidOperationException();

        public byte[] Seal(byte[] header, byte[] payload) => throw new InvalidOperationException();

        public OpenResult Open(byte[] packet) => throw new InvalidOperationException();
    }
}
