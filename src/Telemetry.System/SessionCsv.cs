using System.Globalization;
using System.Text;
using Telemetry.Core;

namespace Telemetry.System;

/// <summary>
/// Читання і запис кадрів сеансу у CSV для Runner. Читання потокове (C2 п.4): файл не вантажиться в пам'ять
/// цілком. Некоректний рядок пропускається і записується в журнал (C2 п.1); розбір рядка — <see cref="Frame.ParseCsv"/>.
/// Власник: тімлід.
/// </summary>
public static class SessionCsv
{
    /// <summary>Заголовок CSV кадрів (без мітки класу).</summary>
    public const string Header = "timestamp_ms,altitude_dm,temp_c10,vibration,mode,critical";

    /// <summary>Читає кадри з CSV, пропускаючи заголовок, порожні й некоректні рядки.</summary>
    /// <param name="path">Шлях до CSV (session.csv).</param>
    /// <param name="log">Журнал прогону.</param>
    /// <param name="parse">Розбір рядка; за замовчуванням <see cref="Frame.ParseCsv"/>.</param>
    /// <exception cref="NotImplementedException">Розбір ще не реалізовано (прогін без кадрів неможливий).</exception>
    public static IReadOnlyList<Frame> Load(string path, RunLog log, Func<string, Frame>? parse = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(log);
        parse ??= Frame.ParseCsv;

        var frames = new List<Frame>();
        var lineNumber = 0;
        var skipped = 0;
        foreach (var line in File.ReadLines(path))
        {
            lineNumber++;
            if (lineNumber == 1 && line.StartsWith("timestamp_ms", StringComparison.Ordinal))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                frames.Add(parse(line));
            }
            catch (Exception ex) when (ex is not NotImplementedException)
            {
                skipped++;
                log.Warn(string.Create(
                    CultureInfo.InvariantCulture,
                    $"{Path.GetFileName(path)}: рядок {lineNumber} пропущено ({ex.GetType().Name}: {ex.Message})"));
            }
        }

        log.Info(string.Create(
            CultureInfo.InvariantCulture,
            $"{Path.GetFileName(path)}: прочитано {frames.Count} кадрів, пропущено рядків: {skipped}"));
        return frames;
    }

    /// <summary>Записує кадри у CSV атомарно (C5 п.4).</summary>
    public static void Save(string path, IEnumerable<Frame> frames)
    {
        ArgumentNullException.ThrowIfNull(frames);
        var text = new StringBuilder();
        text.Append(Header).Append('\n');
        foreach (var f in frames)
        {
            text.Append(CultureInfo.InvariantCulture, $"{f.TimestampMs},{f.AltitudeDm},{f.TempC10},{f.Vibration},{f.Mode},{f.Critical}")
                .Append('\n');
        }

        AtomicFile.WriteAllText(path, text.ToString());
    }
}
