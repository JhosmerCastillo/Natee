using Natee.Core.Models;

namespace Natee.Core.Interfaces;

/// <summary>
/// Collects active network interface configuration, observed byte counters, throughput, and connectivity probe samples.
/// </summary>
public interface INetworkMetricsProvider
{
    Task<NetworkSnapshot> CaptureSnapshotAsync(
        IReadOnlyList<string> probeTargets,
        int probeTimeoutMs,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Executes comprehensive one-click network diagnostics across interface, gateway, DNS, remote targets, and optional route trace.
/// </summary>
public interface INetworkDiagnosticsService
{
    Task<DiagnosticReport> RunDiagnosisAsync(
        NateeSettings settings,
        IProgress<string>? stepProgress = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Performs an explicit user-triggered Internet download and upload speed test against a documented endpoint.
/// </summary>
public interface ISpeedTestService
{
    Task<SpeedTestResult> RunSpeedTestAsync(
        NateeSettings settings,
        IProgress<SpeedTestProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Provides overall CPU utilization percentage and representative current operating frequency.
/// </summary>
public interface ICpuMetricsProvider
{
    Task<(string ModelName, int LogicalCores, double UsagePercent, double? CurrentFrequencyMHz)> GetCpuMetricsAsync(
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Provides physical RAM capacity, used memory, available memory, and utilization percentage.
/// </summary>
public interface IMemoryMetricsProvider
{
    Task<(long TotalBytes, long UsedBytes, long AvailableBytes, double UsagePercent)> GetMemoryMetricsAsync(
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Provides mounted storage volume capacities, remaining space, and optional read/write throughput.
/// </summary>
public interface IDiskMetricsProvider
{
    Task<IReadOnlyList<StorageVolumeMetric>> GetStorageVolumesAsync(
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Provides hardware sensor readings such as CPU package/core temperature without requiring kernel drivers or administrator privileges.
/// </summary>
public interface IHardwareSensorProvider
{
    Task<(double? TemperatureCelsius, SensorAvailabilityStatus Status, string Note)> GetCpuTemperatureAsync(
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Provides top resource-consuming processes and operating-system uptime.
/// </summary>
public interface IProcessMonitorProvider
{
    Task<IReadOnlyList<ProcessResourceMetric>> GetTopProcessesAsync(
        int maxEntries,
        ProcessSortMode sortMode,
        CancellationToken cancellationToken = default);

    TimeSpan GetSystemUptime();
}

/// <summary>
/// Dispatches native desktop notifications and tracks notification capability on the current platform.
/// </summary>
public interface INotificationService
{
    bool IsSupported { get; }
    string CapabilityStatusNote { get; }
    Task<bool> SendNotificationAsync(AlertNotification notification, CancellationToken cancellationToken = default);
}

/// <summary>
/// Generates human-readable PDF and machine-readable JSON technical reports with privacy redactions applied.
/// </summary>
public interface IReportGeneratorService
{
    Task GeneratePdfReportAsync(
        TechnicalReportDocument redactedDocument,
        string outputFilePath,
        CancellationToken cancellationToken = default);

    Task<string> GenerateJsonReportAsync(
        TechnicalReportDocument redactedDocument,
        string outputFilePath,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Persists diagnostic history, speed test records, and alert logs in a local SQLite database.
/// </summary>
public interface ILocalPersistenceService
{
    string DatabaseFilePath { get; }
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task SaveDiagnosticReportAsync(DiagnosticReport report, CancellationToken cancellationToken = default);
    Task SaveSpeedTestResultAsync(SpeedTestResult result, CancellationToken cancellationToken = default);
    Task SaveAlertAsync(AlertNotification alert, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DiagnosticReport>> GetRecentDiagnosticsAsync(int limit = 25, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SpeedTestResult>> GetRecentSpeedTestsAsync(int limit = 25, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AlertNotification>> GetRecentAlertsAsync(int limit = 50, CancellationToken cancellationToken = default);
    Task PruneExpiredHistoryAsync(int retentionDays, CancellationToken cancellationToken = default);
    Task ClearAllHistoryAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Loads and persists user settings in the platform-appropriate application data directory.
/// </summary>
public interface ISettingsService
{
    string SettingsFilePath { get; }
    NateeSettings CurrentSettings { get; }
    Task<NateeSettings> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(NateeSettings settings, CancellationToken cancellationToken = default);
}

/// <summary>
/// Replaceable integration boundary for future IT Support ticketing systems (e.g., Zammad, Jira Service Management) in Version 2.
/// In Version 1 (MVP), reports are exported locally for manual ticket attachment and no remote upload occurs.
/// </summary>
public interface ITicketIntegrationBoundary
{
    string ProviderName { get; }
    bool IsConfigured { get; }
    Task<(bool Supported, string Message)> PrepareTicketAttachmentBundleAsync(
        string pdfReportPath,
        string jsonReportPath,
        CancellationToken cancellationToken = default);
}
