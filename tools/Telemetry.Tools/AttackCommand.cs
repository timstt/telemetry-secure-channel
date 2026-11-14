using System.Globalization;
using System.Text;
using Telemetry.Attack;
using Telemetry.Core;
using Telemetry.Huffman;
using Telemetry.Packet;
using Telemetry.Security;

namespace Telemetry.Tools;

/// <summary>
/// Модуль атаки (тиждень 12):
/// <list type="bullet">
/// <item><c>attack vigenere|substitution|xor &lt;шифротекст&gt; &lt;корпус&gt; [--lang en|uk]</c> — ГА шукає ключ слабкого шифру
/// за правдоподібністю триграм (корпус — відкритий текст тієї ж мови);</item>
/// <item><c>attack tamper [пакет.bin …] [--password p]</c> — зміна байтів перехоплених пакетів і перевірка реакції приймача.</item>
/// </list>
/// </summary>
internal static class AttackCommand
{
    public static int Run(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("attack vigenere|substitution|xor <шифротекст> <корпус> [--lang en|uk] | attack tamper [пакети…] [--password p]");
            return 1;
        }

        return args[0] == "tamper" ? Tamper(args[1..]) : Break(args);
    }

    private static int Break(string[] args)
    {
        if (args.Length < 3)
        {
            Console.Error.WriteLine("потрібні: шифр, файл шифротексту, файл корпусу");
            return 1;
        }

        var alphabet = Option(args, "--lang") == "uk" ? Alphabet.Ukrainian : Alphabet.English;
        var model = new NgramModel(File.ReadAllText(args[2]), alphabet, 3);
        AttackResult result = args[0] switch
        {
            "vigenere" => KeySearch.BreakVigenere(File.ReadAllText(args[1]), model),
            "substitution" => KeySearch.BreakSubstitution(File.ReadAllText(args[1]), model),
            "xor" => KeySearch.BreakXor(File.ReadAllBytes(args[1]), model),
            _ => throw new ArgumentException($"невідомий шифр «{args[0]}»"),
        };

        var key = args[0] switch
        {
            "xor" => Convert.ToHexString([.. result.Key.Select(b => (byte)b)]),
            _ => new string([.. result.Key.Select(i => alphabet.Letters[i])]),
        };
        Console.WriteLine($"ключ: {key}");
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"пристосованість: {result.Fitness:F2} (покоління 0: {result.BestByGeneration[0]:F2})"));
        Console.WriteLine("--- відкритий текст ---");
        Console.WriteLine(result.Plaintext);
        return 0;
    }

    private static int Tamper(string[] args)
    {
        var root = RepoPaths.Root();
        var password = Option(args, "--password") ?? TeamConfig.Load(Path.Combine(root, "data", "team_config.json")).SessionPassword;
        var files = args.Where((a, i) => !a.StartsWith("--", StringComparison.Ordinal) && (i == 0 || args[i - 1] != "--password")).ToList();
        if (files.Count == 0)
        {
            files = [.. Directory.EnumerateFiles(Path.Combine(root, "data", "reference"), "ref_*.bin").Order(StringComparer.Ordinal)];
        }

        var kdf = new Kdf();
        var report = IntegrityAttack.Run(files.Select(File.ReadAllBytes), () =>
        {
            var rx = new Receiver(new SessionCrypto(password, kdf), new HuffmanCodec());
            return rx.Accept;
        });

        var sb = new StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture, $"Пакетів: {files.Count}, модифікацій: {report.Attempts}, прийнято: {report.Accepted}, винятків: {report.Exceptions}");
        foreach (var (code, count) in report.ByError.OrderBy(p => p.Key))
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"  {code,-9} {count}");
        }

        sb.AppendLine(report.Passed ? "Атака на цілісність не вдалася: кожну модифікацію відхилено." : "УВАГА: приймач прийняв модифікований пакет або впав.");
        Console.Write(sb.ToString());
        return report.Passed ? 0 : 1;
    }

    private static string? Option(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
