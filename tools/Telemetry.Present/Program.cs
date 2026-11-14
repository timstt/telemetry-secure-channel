// Презентаційний режим: локальна сторінка з даними всіх зон за ланцюжком обробки і красивим виводом у консолі.
//
//   dotnet run --project tools/Telemetry.Present [-- --port 5050] [--no-open]
//
// Кнопка «Запустити прогін» виконує справжній Runner (3 сценарії × 5 seed). Якщо прогін не вдався, на екрані
// лишаються попередні результати і з'являється повідомлення.
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Telemetry.Present;

Console.OutputEncoding = new UTF8Encoding(false);
var port = 5050;
var open = true;
for (var i = 0; i < args.Length; i++)
{
    if (args[i] == "--no-open")
    {
        open = false;
    }
    else if (args[i] == "--port" && i + 1 < args.Length && int.TryParse(args[i + 1], NumberStyles.None, CultureInfo.InvariantCulture, out var p))
    {
        port = p;
        i++;
    }
    else
    {
        Console.Error.WriteLine("Використання: dotnet run --project tools/Telemetry.Present -- [--port N] [--no-open]");
        return 2;
    }
}

Terminal.Banner();
List<Preflight.Item> checks;
try
{
    checks = Preflight.Run();
}
catch (DirectoryNotFoundException ex)
{
    Console.Error.WriteLine("  " + Terminal.Red("✗ ") + ex.Message);
    return 2;
}

foreach (var c in checks)
{
    Terminal.Check(c.Name, c.Ok, c.Detail);
}

var live = new LiveRun();
live.Completed += Terminal.Results;
live.LoadSnapshot();
if (!live.HasData)
{
    Console.WriteLine();
    Console.WriteLine("  Збережених результатів ще немає, запускаю перший прогін у фоні…");
    live.TryStart();
}
else
{
    var snapshot = live.State()["results"]!.ToJsonString();
    Terminal.Results(snapshot);
}

Console.WriteLine("  Готую дані зон (Гаффман, рюкзак, ГА, перцептрон, захист)…");
var overview = DemoData.BuildJson();
var preflightJson = JsonSerializer.Serialize(checks, DemoData.Json);

var server = new Server(live, overview, preflightJson);
if (!server.Start(port))
{
    Console.Error.WriteLine("  " + Terminal.Red("✗") + $" не вдалося зайняти порт {port}…{port + 9}. Закрийте інший екземпляр або вкажіть --port.");
    return 1;
}

var url = $"http://localhost:{server.Port}/";
Console.WriteLine();
Console.WriteLine("  " + Terminal.Bold("Сторінка: ") + Terminal.Cyan(url) + Terminal.Dim("   (Ctrl+C — зупинити)"));
Console.WriteLine();

if (open)
{
    OpenBrowser(url);
}

using var stop = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    stop.Cancel();
};
await server.RunAsync(stop.Token).ConfigureAwait(false);
return 0;

static void OpenBrowser(string url)
{
    try
    {
        var psi = OperatingSystem.IsMacOS()
            ? new ProcessStartInfo("open", url)
            : OperatingSystem.IsWindows()
                ? new ProcessStartInfo("cmd", $"/c start {url}")
                : new ProcessStartInfo("xdg-open", url);
        psi.UseShellExecute = false;
        psi.CreateNoWindow = true;
        using var process = Process.Start(psi);
    }
    catch (Exception ex) when (ex is global::System.ComponentModel.Win32Exception or InvalidOperationException)
    {
        Console.WriteLine("  Браузер не відкрився сам: відкрийте адресу вручну.");
    }
}
