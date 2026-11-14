// Інструменти:
//   check-ref [тека]  — CheckRefPackets: усі ref_* приймаються, bad_* дають код з expected_failures.json [тімлід]
//   measure-q [--seeds a,b,…] [--scenarios x,y,…] [--ablation] — Q за сценаріями і seed (таблиці 4 і 2) [тімлід]
//   optimize [--pop N] [--gens G] — ГА проти випадкового пошуку на справжньому Q, журнали в results/ga/ [Георгій]
//   report     — MakeReportTables: таблиці звіту лише з реальних даних [Саша]
//   train-classifier [seed,seed,…] — навчання перцептрона, ваги в src/Telemetry.Predict/classifier.json [Ваня]
using System.Text;

Console.OutputEncoding = new UTF8Encoding(false);
var command = args.Length > 0 ? args[0] : "";
switch (command)
{
    case "report":
        return Telemetry.Tools.ReportTables.Run();
    case "train-classifier":
        return Telemetry.Tools.TrainClassifier.Run(args[1..]);
    case "check-ref":
        return Telemetry.Tools.CheckRefPackets.Run(args[1..]);
    case "measure-q":
        return Telemetry.Tools.MeasureQ.Run(args[1..]);
    case "optimize":
        return Telemetry.Tools.MeasureQ.Optimize(args[1..]);
    default:
        Console.Error.WriteLine("Використання: dotnet run --project tools/Telemetry.Tools -- check-ref|measure-q|optimize|report|train-classifier");
        return 1;
}
