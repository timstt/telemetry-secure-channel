using System.Diagnostics;
using System.Runtime.InteropServices;
using Telemetry.System;

namespace Telemetry.Present;

/// <summary>Перевірка перед показом: усе, без чого прогін чи сторінка не запрацюють.</summary>
internal static class Preflight
{
    public sealed record Item(string Name, bool Ok, string Detail);

    public static List<Item> Run()
    {
        var items = new List<Item>
        {
            new(".NET", true, RuntimeInformation.FrameworkDescription),
            PythonCheck(),
        };
        foreach (var (label, path) in new[]
        {
            ("team_config.json", Repo.Config),
            ("session.csv", Repo.Session),
            ("channel.py", Repo.Path("channel", "channel.py")),
            ("bridge.py", Repo.Path("channel", "bridge.py")),
            ("результати ГА", Repo.Path("results", "ga", "summary.md")),
            ("результати перцептрона", Repo.Path("results", "classifier.md")),
            ("таблиця впливу блоків", Repo.Path("results", "ablation.md")),
        })
        {
            items.Add(new Item(label, File.Exists(path), File.Exists(path) ? "знайдено" : "немає файла: " + Path.GetRelativePath(Repo.Root, path)));
        }

        return items;
    }

    private static Item PythonCheck()
    {
        try
        {
            var psi = new ProcessStartInfo(ChannelBridge.DefaultPython(), "--version")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var process = Process.Start(psi);
            if (process is null || !process.WaitForExit(5000))
            {
                return new Item("Python 3 (channel.py)", false, "не відповідає");
            }

            var text = (process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd()).Trim();
            return new Item("Python 3 (channel.py)", process.ExitCode == 0, text);
        }
        catch (Exception ex) when (ex is global::System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return new Item("Python 3 (channel.py)", false, "не знайдено: " + ex.Message);
        }
    }
}
