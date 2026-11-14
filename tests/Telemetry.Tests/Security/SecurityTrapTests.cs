using System.Text.Json;
using Telemetry.Security;
using Telemetry.Tests.Infrastructure;

namespace Telemetry.Tests.Security;

/// <summary>Пастки 7–9 (зона Колі).</summary>
public class SecurityTrapTests
{
    private static readonly byte[] Salt = TestData.Hex("000102030405060708090A0B0C0D0E0F");

    [Fact]
    public void Trap07_kdf_keys_are_different_and_cached()
    {
        var kdf = new Kdf();
        var (kEnc, kMac) = kdf.Derive("algo2026", Salt);
        Assert.Equal(32, kEnc.Length);
        Assert.Equal(32, kMac.Length);
        Assert.NotEqual(kEnc, kMac);
        Assert.Same(kEnc, kdf.Derive("algo2026", Salt).KEnc); // кеш на сеанс, без повторних 100000 ітерацій
    }

    [Fact]
    public void Trap08_ctr_counter_starts_at_1_ref01_ciphertext()
    {
        var packet = File.ReadAllBytes(TestData.Reference("ref_01_single_frame.bin"));
        var (kEnc, _) = new Kdf().Derive("algo2026", Salt);
        var plaintext = TestData.Hex(
            "0001 00 00 0007 0001 0103 0304 D704 E804 FD04 FF03 0000000C 00000004 0CE6DF90");
        var ct = CtrCipher.Transform(kEnc, TestData.Hex("010203040506070800000000"), plaintext);
        Assert.Equal(packet[43..75], ct);
        Assert.Equal(plaintext, CtrCipher.Transform(kEnc, TestData.Hex("010203040506070800000000"), ct));
    }

    [Fact]
    public void Trap09_hmac_covers_header_and_ciphertext()
    {
        var packet = File.ReadAllBytes(TestData.Reference("ref_01_single_frame.bin"));
        var (_, kMac) = new Kdf().Derive("algo2026", Salt);
        Assert.True(Mac.Verify(kMac, packet.AsSpan(0, 75), packet.AsSpan(75)));
        packet[5] ^= 1; // зміна заголовка теж ламає тег
        Assert.False(Mac.Verify(kMac, packet.AsSpan(0, 75), packet.AsSpan(75)));
    }

    [Fact]
    public void Ref01_metadata_matches_spec_example()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(TestData.Reference("ref_01_single_frame.json")));
        Assert.Equal("0ce6df90", doc.RootElement.GetProperty("bitstream_hex").GetString());
        Assert.Equal("algo2026", doc.RootElement.GetProperty("пароль").GetString());
    }
}
