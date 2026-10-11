namespace Natee.App.Localization;

/// <summary>
/// Centralized English user-interface strings structured for straightforward localization into Spanish and other languages
/// without modifying domain or view-model logic.
/// </summary>
public static class UiStrings
{
    public const string AppName = "Natee";
    public const string Tagline = "Calm monitoring. Clear diagnostics.";
    public const string VersionLabel = "v1.0.0 MVP";

    // Tray Menu Actions
    public const string TrayOpenDashboard = "Open Dashboard";
    public const string TrayRunDiagnosis = "Run Network Diagnosis";
    public const string TrayRunSpeedTest = "Run Speed Test";
    public const string TrayGenerateReport = "Generate Report";
    public const string TraySettings = "Settings";
    public const string TrayExit = "Exit";

    // Explanatory notes
    public const string LiveThroughputExplanation =
        "Live throughput shows currently observed network traffic on the active interface, not your maximum available Internet bandwidth.";

    public const string SpeedTestDisclosure =
        "Speed tests transfer real data (~15 MB download, ~5 MB upload) over HTTPS to the configured test endpoint (default: Cloudflare Edge) and expose your public IP to that server. Speed tests only run when you explicitly click 'Run Speed Test'.";

    public const string ProcessCpuExplanation =
        "Process CPU utilization is normalized across all logical processors (0–100%). Physical memory reflects current resident working set.";

    public const string CpuMetricsExplanation =
        "CPU utilization is sampled from OS kernel counters (/proc/stat on Linux, GetSystemTimes on Windows). Frequency reflects real-time operating clock speed where exposed. Temperature uses non-invasive OS sensors without kernel drivers or root privileges.";
}
