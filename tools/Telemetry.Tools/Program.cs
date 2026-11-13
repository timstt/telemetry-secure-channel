// Інструменти:
//   check-ref  — CheckRefPackets: усі ref_* приймаються, bad_* дають код з expected_failures.json [тімлід]
//   measure-q  — MeasureQ: Q за сценаріями і seed [тімлід]
//   report     — MakeReportTables: таблиці звіту лише з реальних даних [Саша]
// Поки що — заглушка каркаса.
var command = args.Length > 0 ? args[0] : "";
switch (command)
{
    case "report":
        return Telemetry.Tools.ReportTables.Run();
    case "check-ref":
    case "measure-q":
        Console.Error.WriteLine($"{command}: ще не реалізовано (каркас).");
        return 2;
    default:
        Console.Error.WriteLine("Використання: dotnet run --project tools/Telemetry.Tools -- check-ref|measure-q|report");
        return 1;
}
