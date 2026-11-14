using System.Text;
using Telemetry.Attack;
using Telemetry.Core;
using Telemetry.Huffman;
using Telemetry.Packet;
using Telemetry.Security;
using Telemetry.Tests.Infrastructure;

namespace Telemetry.Tests.Attack;

/// <summary>Модуль атаки (тиждень 12): ГА проти слабких шифрів і атака на цілісність приймача.</summary>
public class AttackTests
{
    /// <summary>Корпус для моделі мови (власний текст команди, не частина атакованих повідомлень).</summary>
    private const string Corpus =
        "The ground station listens to the small satellite every time it rises above the horizon. " +
        "Each pass lasts only a few minutes, so the team decided long ago that the radio link must carry " +
        "the most important measurements first and leave the routine numbers for later. When the weather " +
        "is calm the channel is generous and every frame arrives on time, but during a storm many packets " +
        "are lost and the operators have to choose what to keep. The engineers wrote a simple rule: if a " +
        "reading shows that something is wrong with the battery, the heater or the engine, it goes to the " +
        "front of the queue. Ordinary temperature samples can wait. This idea sounds obvious, yet it took " +
        "several weeks of testing before the software behaved the way the people in the control room " +
        "expected. They also learned that compression is useful only when the data has a pattern; random " +
        "noise does not shrink, and trying to squeeze it wastes both time and bits. Security was the last " +
        "piece of the puzzle. Without a secret key anyone with an antenna could read the telemetry or, " +
        "worse, send false commands that look real. With a proper cipher and an authentication tag the " +
        "receiver rejects any packet that was changed on the way, even if only one bit is different. " +
        "The students who built the system were proud of it, and they invited their friends to try to " +
        "break it during the week of attacks at the end of the semester. Most of the attempts failed, " +
        "which was exactly the point of the exercise and the best possible result for the whole group.";

    /// <summary>Повідомлення, яке «перехоплено» (інший текст, ніж корпус).</summary>
    private const string Secret =
        "During the night the operators noticed that the temperature of the battery was rising faster than " +
        "usual. They asked the satellite to send every critical frame twice and to skip the ordinary ones " +
        "until the problem was understood. After two more passes the readings became stable again, and the " +
        "team wrote a short report that explained what had happened and what they would do next time.";

    private static readonly NgramModel Model = new(Corpus + " " + Corpus.ToUpperInvariant(), Alphabet.English, 3);

    [Fact]
    public void Vigenere_key_is_recovered_by_ga()
    {
        var key = Vigenere.Key("orbit", Alphabet.English);
        var cipher = Vigenere.Encrypt(Secret, key, Alphabet.English);
        Assert.NotEqual(Secret, cipher);
        Assert.Equal(Secret, Vigenere.Decrypt(cipher, key, Alphabet.English));

        var result = KeySearch.BreakVigenere(cipher, Model, maxKeyLength: 10, new KeyGaSettings(Population: 60, Generations: 120));
        Assert.Equal(key, result.Key);
        Assert.Equal(Secret, result.Plaintext);
        Assert.True(result.BestByGeneration[^1] >= result.BestByGeneration[0]); // елітизм
    }

    [Fact]
    public void Repeating_key_xor_is_recovered_by_ga()
    {
        var key = "k3Y"u8.ToArray();
        var cipher = XorCipher.Apply(Encoding.UTF8.GetBytes(Secret), key);
        Assert.Equal(Secret, Encoding.UTF8.GetString(XorCipher.Apply(cipher, key)));
        var result = KeySearch.BreakXor(cipher, Model, maxKeyLength: 6, new KeyGaSettings(Population: 80, Generations: 150));
        Assert.Equal(key.Select(b => (int)b), result.Key);
        Assert.Equal(Secret, result.Plaintext);
    }

    [Fact]
    public void Substitution_is_mostly_recovered_by_ga()
    {
        var rng = new Random(42);
        var key = Enumerable.Range(0, 26).ToArray();
        rng.Shuffle(key);
        var text = Secret + " " + Secret.Replace("the", "a", StringComparison.Ordinal);
        var cipher = Substitution.Encrypt(text, key, Alphabet.English);
        Assert.Equal(text, Substitution.Decrypt(cipher, key, Alphabet.English));

        var result = KeySearch.BreakSubstitution(cipher, Model, new KeyGaSettings(Population: 60, Generations: 150, MutationRate: 0.05));
        var letters = Alphabet.English.Normalize(text);
        var recovered = Alphabet.English.Normalize(result.Plaintext);
        var correct = letters.Zip(recovered).Count(p => p.First == p.Second);
        Assert.True(correct >= 0.8 * letters.Length, $"правильно {correct} з {letters.Length}");
    }

    [Fact]
    public void Key_length_estimate_prefers_true_length_over_multiples()
    {
        var key = Vigenere.Key("radio", Alphabet.English);
        var cipher = Alphabet.English.Normalize(Vigenere.Encrypt(Secret + Corpus, key, Alphabet.English));
        var lengths = KeySearch.LikelyKeyLengths(cipher.Select(Alphabet.English.IndexOf).ToArray(), 12);
        Assert.Equal(5, lengths[0]);
    }

    [Fact]
    public void Ciphers_and_model_validate_input_and_keep_non_letters()
    {
        Assert.Throws<ArgumentException>(() => new Alphabet("aa"));
        Assert.Throws<ArgumentException>(() => Vigenere.Key("a1", Alphabet.English));
        Assert.Throws<ArgumentException>(() => Vigenere.Encrypt("x", [], Alphabet.English));
        Assert.Throws<ArgumentException>(() => Substitution.Inverse([0, 0]));
        Assert.Throws<ArgumentException>(() => Substitution.Encrypt("x", [0, 1], Alphabet.English));
        Assert.Throws<ArgumentException>(() => XorCipher.Apply([1], []));
        Assert.Throws<ArgumentException>(() => new NgramModel("ab", Alphabet.English, 3));
        Assert.Equal("Ifmmp, Xpsme!", Vigenere.Encrypt("Hello, World!", [1], Alphabet.English));
        var ua = Vigenere.Encrypt("Привіт, світе", Vigenere.Key("ключ", Alphabet.Ukrainian), Alphabet.Ukrainian);
        Assert.Equal("Привіт, світе", Vigenere.Decrypt(ua, Vigenere.Key("ключ", Alphabet.Ukrainian), Alphabet.Ukrainian));
        Assert.True(Model.ScorePerGram(Secret) > Model.ScorePerGram(Vigenere.Encrypt(Secret, [3, 7], Alphabet.English)));
        Assert.Equal(26, Model.LetterFrequency.Count);
        Assert.Equal(3, Model.N);
    }

    [Fact]
    public void Integrity_attack_on_team_receiver_finds_no_accepted_modification()
    {
        var kdf = new Kdf();
        var packets = new[] { "ref_01_single_frame.bin", "ref_05_no_compression.bin" }
            .Select(n => File.ReadAllBytes(TestData.Reference(n))).ToArray();
        var report = IntegrityAttack.Run(packets, () =>
        {
            var rx = new Receiver(new SessionCrypto("algo2026", kdf), new HuffmanCodec());
            return rx.Accept;
        });

        Assert.True(report.Passed, $"прийнято {report.Accepted}, винятків {report.Exceptions}");
        Assert.True(report.Attempts > 1000);
        Assert.True(report.ByError[PacketError.E_HMAC] > 0);
        Assert.True(report.ByError[PacketError.E_SHORT] > 0);
        Assert.True(report.ByError[PacketError.E_LENGTH] > 0);
    }

    [Fact]
    public void Integrity_attack_detects_a_broken_receiver()
    {
        var packet = File.ReadAllBytes(TestData.Reference("ref_01_single_frame.bin"));
        var naive = IntegrityAttack.Run([packet], () => _ => new OpenResult(PacketError.None, 0, []));
        Assert.False(naive.Passed);
        Assert.Equal(naive.Attempts, naive.Accepted);

        var throwing = IntegrityAttack.Run([packet], () => _ => throw new InvalidOperationException());
        Assert.Equal(throwing.Attempts, throwing.Exceptions);
        Assert.False(throwing.Passed);
    }
}
