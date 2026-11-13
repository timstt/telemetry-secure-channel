using System.Globalization;
using System.Text;

namespace Telemetry.System;

/// <summary>
/// Журнал повного прогону (C2 п.3, C5 п.3): кожен виняток записується з типом, повідомленням і стеком,
/// а секрети (пароль сеансу) замінюються на <c>***</c> ще до запису. Потокобезпечний. Власник: тімлід.
/// </summary>
/// <remarks>
/// Слово <c>Unhandled</c> пише лише <see cref="Unhandled"/> — обробник необроблених винятків процесу.
/// Тест C2 п.3 перевіряє, що після прогону його в журналі немає.
/// </remarks>
public sealed class RunLog : IDisposable
{
    /// <summary>Чим замінюються секрети в журналі.</summary>
    public const string Mask = "***";

    private readonly TextWriter _writer;
    private readonly string[] _secrets;
    private readonly object _gate = new();
    private bool _disposed;

    /// <summary>Журнал у файл (перезаписується на кожен прогін).</summary>
    /// <param name="path">Шлях до файла журналу.</param>
    /// <param name="secrets">Рядки, які ніколи не потрапляють у журнал.</param>
    public RunLog(string path, IEnumerable<string> secrets)
        : this(OpenFile(path), secrets)
    {
    }

    /// <summary>Журнал у довільний <see cref="TextWriter"/> (для тестів).</summary>
    public RunLog(TextWriter writer, IEnumerable<string> secrets)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(secrets);
        _writer = writer;
        _secrets = [.. secrets.Where(s => !string.IsNullOrEmpty(s)).OrderByDescending(s => s.Length)];
    }

    /// <summary>Кількість записаних помилок (винятків).</summary>
    public int ErrorCount { get; private set; }

    /// <summary>Інформаційний рядок.</summary>
    public void Info(string message) => Write("INFO", message);

    /// <summary>Попередження (наприклад, пропущений рядок вхідних даних).</summary>
    public void Warn(string message) => Write("WARN", message);

    /// <summary>Оброблений виняток: контекст, тип, повідомлення, стек (разом із вкладеними).</summary>
    public void Error(string context, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        lock (_gate)
        {
            ErrorCount++;
        }

        Write("ERROR", context + ": " + exception);
    }

    /// <summary>Необроблений виняток процесу (лише з обробника AppDomain.UnhandledException).</summary>
    public void Unhandled(object? exception) => Write("FATAL", "Unhandled exception: " + exception);

    /// <summary>Замінює всі секрети в рядку на <see cref="Mask"/>.</summary>
    public string Redact(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        foreach (var secret in _secrets)
        {
            text = text.Replace(secret, Mask, StringComparison.Ordinal);
        }

        return text;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _writer.Dispose();
        }
    }

    private void Write(string level, string message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var line = string.Create(
            CultureInfo.InvariantCulture,
            $"{DateTimeOffset.UtcNow:yyyy-MM-ddTHH:mm:ss.fffZ} {level} {Redact(message)}");
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _writer.WriteLine(line);
            _writer.Flush();
        }
    }

    private static StreamWriter OpenFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        return new StreamWriter(path, append: false, new UTF8Encoding(false)) { NewLine = "\n" };
    }
}
