namespace Natee.Infrastructure.Platform;

/// <summary>
/// Resolves platform-appropriate application data directories for SQLite storage, settings, and logs.
/// Ensures no runtime files are ever written to the source repository or installation directory.
/// </summary>
public static class AppDataPaths
{
    public static string GetAppDataDirectory(string? customBaseDirectory = null)
    {
        if (!string.IsNullOrWhiteSpace(customBaseDirectory))
        {
            Directory.CreateDirectory(customBaseDirectory);
            return customBaseDirectory;
        }

        string? envOverride = Environment.GetEnvironmentVariable("NATEE_APPDATA_DIR");
        if (!string.IsNullOrWhiteSpace(envOverride))
        {
            Directory.CreateDirectory(envOverride);
            return envOverride;
        }

        string baseFolder = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(baseFolder))
        {
            baseFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
        }

        string nateeDir = Path.Combine(baseFolder, "Natee");
        try
        {
            Directory.CreateDirectory(nateeDir);
            return nateeDir;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            string fallbackDir = Path.Combine(Path.GetTempPath(), "Natee");
            Directory.CreateDirectory(fallbackDir);
            return fallbackDir;
        }
    }

    public static string GetDatabaseFilePath(string? customBaseDirectory = null)
        => Path.Combine(GetAppDataDirectory(customBaseDirectory), "natee_history.db");

    public static string GetSettingsFilePath(string? customBaseDirectory = null)
        => Path.Combine(GetAppDataDirectory(customBaseDirectory), "settings.json");

    public static string GetDefaultReportsDirectory(string? customBaseDirectory = null)
    {
        string reportsDir = Path.Combine(GetAppDataDirectory(customBaseDirectory), "Reports");
        try
        {
            Directory.CreateDirectory(reportsDir);
            return reportsDir;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            string fallbackReports = Path.Combine(Path.GetTempPath(), "Natee", "Reports");
            Directory.CreateDirectory(fallbackReports);
            return fallbackReports;
        }
    }
}
