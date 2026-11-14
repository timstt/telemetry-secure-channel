using System.Globalization;

namespace Telemetry.Present;

/// <summary>Розбір markdown-таблиць (рядки з «|»), які генерують наші інструменти.</summary>
internal static class MarkdownTable
{
    /// <summary>Усі таблиці тексту: кожна — список рядків без роздільника; клітинки обрізано.</summary>
    public static List<List<string[]>> ParseAll(string text)
    {
        var tables = new List<List<string[]>>();
        List<string[]>? current = null;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (!line.StartsWith('|'))
            {
                current = null;
                continue;
            }

            var cells = line.Trim('|').Split('|').Select(c => c.Trim().Trim('*')).ToArray();
            if (cells.All(c => c.Length > 0 && c.All(ch => ch is '-' or ':')))
            {
                continue;
            }

            if (current is null)
            {
                current = [];
                tables.Add(current);
            }

            current.Add(cells);
        }

        return tables;
    }

    public static double Number(string cell) =>
        double.Parse(cell.Replace('%', ' ').Trim(), NumberStyles.Float, CultureInfo.InvariantCulture);
}
