using System.Text;

namespace Telemetry.System;

/// <summary>
/// Атомарний запис файлів результатів (C5 п.4): вміст пишеться у <c>&lt;файл&gt;.tmp</c>, скидається на диск
/// і лише потім замінює цільовий файл через <see cref="File.Move(string, string, bool)"/>.
/// Збій посередині запису не псує попередній коректний файл, а тимчасовий файл прибирається.
/// Власник: тімлід.
/// </summary>
public static class AtomicFile
{
    private static readonly UTF8Encoding Utf8 = new(false);

    /// <summary>Записує файл атомарно: <paramref name="write"/> отримує потік тимчасового файла.</summary>
    /// <param name="path">Цільовий файл; теку буде створено за потреби.</param>
    /// <param name="write">Запис вмісту. Виняток з нього скасовує заміну цільового файла.</param>
    public static void Write(string path, Action<Stream> write)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(write);

        var target = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(target);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temp = target + ".tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                write(stream);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temp, target, overwrite: true);
        }
        catch
        {
            TryDelete(temp);
            throw;
        }
    }

    /// <summary>Записує текст у UTF-8 без BOM атомарно.</summary>
    public static void WriteAllText(string path, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        Write(path, stream => stream.Write(Utf8.GetBytes(text)));
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // Тимчасовий файл не завадить наступному запису: FileMode.Create його перезапише.
        }
        catch (UnauthorizedAccessException)
        {
            // Те саме.
        }
    }
}
