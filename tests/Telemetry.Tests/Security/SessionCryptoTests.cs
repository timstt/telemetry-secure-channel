using System.Text.Json;
using Telemetry.Core;
using Telemetry.Security;
using Telemetry.Tests.Infrastructure;

namespace Telemetry.Tests.Security;

/// <summary>Seal/Open за FORMAT_SPEC §4–5 (зона Колі): байт у байт з ref_01, порядок перевірок, C5 п.1–2.</summary>
public class SessionCryptoTests
{
    private const string Password = "algo2026";

    /// <summary>Спільний KDF: PBKDF2 на 100000 ітерацій виконується один раз на клас тестів.</summary>
    private static readonly Kdf SharedKdf = new();

    private static readonly byte[] Ref01Plaintext = TestData.Hex(
        "0001 00 00 0007 0001 0103 0304 D704 E804 FD04 FF03 0000000C 00000004 0CE6DF90");

    private static byte[] Ref(string name) => File.ReadAllBytes(TestData.Reference(name));

    private static SessionCrypto Crypto(string password = Password) => new(password, SharedKdf);

    [Fact]
    public void Seal_reproduces_ref01_byte_for_byte()
    {
        var packet = Ref("ref_01_single_frame.bin");
        Assert.Equal(107, packet.Length);
        Assert.Equal(packet, Crypto().Seal(packet[..43], Ref01Plaintext));
    }

    [Fact]
    public void Open_ref01_gives_plaintext_and_seq()
    {
        var r = Crypto().Open(Ref("ref_01_single_frame.bin"));
        Assert.True(r.Ok);
        Assert.Equal(0u, r.PacketSeq);
        Assert.Equal(Ref01Plaintext, r.Payload);
    }

    [Fact]
    public void Open_then_seal_roundtrip_for_all_reference_packets()
    {
        foreach (var name in new[] { "ref_02_empty", "ref_03_degenerate", "ref_04_session", "ref_05_no_compression", "ref_06_class_table" })
        {
            var packet = Ref(name + ".bin");
            var r = Crypto().Open(packet);
            Assert.True(r.Ok, name);
            Assert.Equal(packet, Crypto().Seal(packet[..43], r.Payload!));
        }
    }

    [Theory]
    [InlineData("bad_01_tampered_byte.bin", PacketError.E_HMAC)]
    [InlineData("bad_02_tampered_tag.bin", PacketError.E_HMAC)]
    [InlineData("bad_03_truncated.bin", PacketError.E_LENGTH)]
    [InlineData("bad_04_bad_magic.bin", PacketError.E_MAGIC)]
    public void Bad_packets_are_rejected_before_decryption(string file, PacketError code)
    {
        var ctrCalls = 0;
        var crypto = new SessionCrypto(Password, SharedKdf, (k, n, d) =>
        {
            ctrCalls++;
            return CtrCipher.Transform(k, n, d);
        });
        var r = crypto.Open(Ref(file));
        Assert.Equal(code, r.Error);
        Assert.Null(r.Payload);
        Assert.Equal(0, ctrCalls); // C5 п.1–2: заголовок і HMAC перевіряються до CTR
    }

    [Fact]
    public void Wrong_password_is_E_HMAC_and_ctr_is_never_called()
    {
        var crypto = new SessionCrypto("wrong", SharedKdf, (_, _, _) => throw new InvalidOperationException("CTR не мав викликатися"));
        var r = crypto.Open(Ref("ref_01_single_frame.bin"));
        Assert.Equal(PacketError.E_HMAC, r.Error);
        Assert.Null(r.Payload);
    }

    [Fact]
    public void Every_prefix_of_a_packet_is_rejected_without_exception()
    {
        var packet = Ref("ref_01_single_frame.bin");
        var crypto = Crypto();
        for (var n = 0; n < packet.Length; n++)
        {
            var r = crypto.Open(packet[..n]);
            Assert.Equal(n < 75 ? PacketError.E_SHORT : PacketError.E_LENGTH, r.Error);
        }

        Assert.Equal(PacketError.E_SHORT, crypto.Open(null!).Error);
    }

    [Fact]
    public void Every_single_byte_change_is_detected()
    {
        var packet = Ref("ref_01_single_frame.bin");
        var crypto = Crypto();
        for (var i = 0; i < packet.Length; i++)
        {
            var copy = (byte[])packet.Clone();
            copy[i] ^= 0x01;
            Assert.False(crypto.Open(copy).Ok, $"байт {i}");
        }
    }

    [Fact]
    public void Huge_ciphertext_len_is_E_LENGTH_without_allocation()
    {
        var packet = Ref("ref_01_single_frame.bin");
        packet[39] = packet[40] = packet[41] = packet[42] = 0xFF;
        Assert.Equal(PacketError.E_LENGTH, Crypto().Open(packet).Error);
    }

    [Fact]
    public void Version_other_than_1_is_E_MAGIC()
    {
        var packet = Ref("ref_01_single_frame.bin");
        packet[4] = 2;
        Assert.Equal(PacketError.E_MAGIC, Crypto().Open(packet).Error);
    }

    [Fact]
    public void Seal_validates_header()
    {
        var header = Ref("ref_01_single_frame.bin")[..43];
        Assert.Throws<ArgumentException>(() => Crypto().Seal(header[..42], Ref01Plaintext));
        Assert.Throws<ArgumentException>(() => Crypto().Seal(header, new byte[5]));
    }

    [Fact]
    public void Kdf_is_derived_once_per_session_and_keys_are_split()
    {
        var kdf = new Kdf();
        var crypto = new SessionCrypto(Password, kdf);
        var packet = Ref("ref_01_single_frame.bin");
        for (var i = 0; i < 5; i++)
        {
            Assert.True(crypto.Open(packet).Ok);
        }

        Assert.Equal(1, kdf.Derivations);
        Assert.Same(kdf, crypto.Kdf);
        var (kEnc, kMac) = crypto.DeriveKeys(Password, packet[11..27]);
        Assert.NotEqual(kEnc, kMac);
        Assert.Equal(1, kdf.Derivations);
        Assert.Throws<ArgumentException>(() => kdf.Derive(Password, new byte[15]));
    }

    [Fact]
    public void Ctr_validates_key_and_nonce_and_handles_partial_blocks()
    {
        var key = new byte[32];
        var nonce = new byte[12];
        Assert.Throws<ArgumentException>(() => CtrCipher.Transform(new byte[16], nonce, []));
        Assert.Throws<ArgumentException>(() => CtrCipher.Transform(key, new byte[8], []));
        Assert.Empty(CtrCipher.Transform(key, nonce, []));
        var data = Enumerable.Range(0, 37).Select(i => (byte)i).ToArray();
        var ct = CtrCipher.Transform(key, nonce, data);
        Assert.Equal(37, ct.Length);
        Assert.Equal(data, CtrCipher.Transform(key, nonce, ct));
    }

    [Fact]
    public void Password_and_keys_do_not_appear_in_sealed_packet()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(TestData.Reference("ref_01_single_frame.json")));
        var packet = Crypto().Seal(Ref("ref_01_single_frame.bin")[..43], Ref01Plaintext);
        var (kEnc, kMac) = SharedKdf.Derive(Password, packet[11..27]);
        Assert.False(Contains(packet, "algo2026"u8.ToArray()));
        Assert.False(Contains(packet, kEnc));
        Assert.False(Contains(packet, kMac));
        Assert.Equal("algo2026", doc.RootElement.GetProperty("пароль").GetString());
    }

    private static bool Contains(byte[] haystack, byte[] needle) =>
        haystack.AsSpan().IndexOf(needle) >= 0;
}
