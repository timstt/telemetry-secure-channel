namespace Telemetry.Present;

/// <summary>Шляхи репозиторію: корінь шукається вгору від поточної теки і від теки програми.</summary>
internal static class Repo
{
    private static readonly Lazy<string> RootLazy = new(FindRoot);

    public static string Root => RootLazy.Value;

    public static string Path(params string[] parts) => global::System.IO.Path.Combine([Root, .. parts]);

    public static string Config => Path("data", "team_config.json");

    public static string Session => Path("data", "session.csv");

    public static string Out => Path("out");

    private static string FindRoot()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
            {
                if (File.Exists(global::System.IO.Path.Combine(dir.FullName, "Telemetry.sln")))
                {
                    return dir.FullName;
                }
            }
        }

        throw new DirectoryNotFoundException("Не знайдено Telemetry.sln: запускайте з теки репозиторію.");
    }
}
