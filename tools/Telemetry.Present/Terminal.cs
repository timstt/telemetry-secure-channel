using System.Globalization;
using System.Text.Json.Nodes;

namespace Telemetry.Present;

/// <summary>Охайний вивід у консолі: кольори лише коли вивід не перенаправлено.</summary>
internal static class Terminal
{
    private static readonly bool Color = !Console.IsOutputRedirected && Environment.GetEnvironmentVariable("NO_COLOR") is null;

    public static string Paint(string text, string code) => Color ? $"\u001b[{code}m{text}\u001b[0m" : text;

    public static string Bold(string text) => Paint(text, "1");

    public static string Dim(string text) => Paint(text, "2");

    public static string Green(string text) => Paint(text, "32");

    public static string Red(string text) => Paint(text, "31");

    public static string Cyan(string text) => Paint(text, "36");

    public static void Banner()
    {
        Console.WriteLine();
        Console.WriteLine("  " + Bold("Захищений канал телеметрії") + Dim("  ·  команда T01"));
        Console.WriteLine("  " + Dim("перцептрон → Гаффман → рюкзак → черга → шифр → канал → приймач"));
        Console.WriteLine();
    }

    public static void Check(string name, bool ok, string detail)
    {
        Console.WriteLine($"  {(ok ? Green("✓") : Red("✗"))} {name,-34} {Dim(detail)}");
    }

    public static void Results(string resultsJson)
    {
        var root = JsonNode.Parse(resultsJson)!;
        Console.WriteLine();
        Console.WriteLine("  " + Bold($"{"сценарій",-12}{"прогонів",10}{"Q",9}{"R",9}{"R мін.",9}"));
        foreach (var s in root["Scenarios"]!.AsArray())
        {
            var done = $"{s!["Completed"]}/{s["Runs"]}";
            var q = ((double)s["MeanQ"]!).ToString("F4", CultureInfo.InvariantCulture);
            var r = ((double)s["MeanR"]!).ToString("F4", CultureInfo.InvariantCulture);
            var rMin = ((double)s["MinR"]!).ToString("F4", CultureInfo.InvariantCulture);
            Console.WriteLine($"  {(string)s["Scenario"]!,-12}{done,10}{Cyan(q.PadLeft(9))}{r,9}{rMin,9}");
        }

        Console.WriteLine("  " + Dim(string.Create(CultureInfo.InvariantCulture, $"час: {(double)root["ElapsedSeconds"]!:F2} с із {(double)root["TimeLimitSeconds"]!:F0} с")));
        Console.WriteLine();
    }
}
