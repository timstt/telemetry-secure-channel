namespace Telemetry.Tests.Infrastructure;

/// <summary>Пошук файлів репозиторію (data/, channel/) незалежно від теки запуску тестів.</summary>
internal static class TestData
{
    /// <summary>Причина пропуску тестів, що чекають реалізації зони.</summary>
    public const string Pending = "Чекає реалізації зони (каркас). Прибрати Skip у PR власника зони.";

    private static readonly Lazy<string> RootLazy = new(FindRoot);

    /// <summary>Корінь репозиторію (тека з Telemetry.sln).</summary>
    public static string Root => RootLazy.Value;

    /// <summary>Шлях відносно кореня репозиторію.</summary>
    public static string Path(params string[] parts) =>
        global::System.IO.Path.Combine([Root, .. parts]);

    /// <summary>Шлях до файла в data/reference.</summary>
    public static string Reference(string name) => Path("data", "reference", name);

    /// <summary>Байти з hex-рядка.</summary>
    public static byte[] Hex(string hex) => Convert.FromHexString(hex.Replace(" ", "", StringComparison.Ordinal));

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(global::System.IO.Path.Combine(dir.FullName, "Telemetry.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new DirectoryNotFoundException("Не знайдено Telemetry.sln вище теки тестів");
    }
}
