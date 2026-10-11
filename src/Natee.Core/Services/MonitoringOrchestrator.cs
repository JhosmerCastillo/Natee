using Natee.Core.Interfaces;
using Natee.Core.Models;

namespace Natee.Core.Services;

/// <summary>
/// Coordinates periodic background network and system monitoring, evaluates intelligent alerts,
/// caches the latest snapshots for instant UI rendering, and ensures deterministic shutdown.
/// </summary>
public sealed class MonitoringOrchestrator : IDisposable
{
    private readonly INetworkMetricsProvider _networkProvider;
    private readonly ICpuMetricsProvider _cpuProvider;
    private readonly IMemoryMetricsProvider _memoryProvider;
    private readonly IDiskMetricsProvider _diskProvider;
    private readonly IHardwareSensorProvider _sensorProvider;
    private readonly IProcessMonitorProvider _processProvider;
    private readonly INotificationService _notificationService;
    private readonly ILocalPersistenceService _persistenceService;
    private readonly ISettingsService _settingsService;
    private readonly NotificationEvaluationEngine _notificationEngine;

    private CancellationTokenSource? _loopCts;
    private Task? _loopTask;
    private readonly SemaphoreSlim _sampleLock = new(1, 1);
    private bool _disposed;

    public NetworkSnapshot LatestNetworkSnapshot { get; private set; } = new();
    public SystemMetricsSnapshot LatestSystemSnapshot { get; private set; } = new();
    public DiagnosticReport? LatestDiagnosticReport { get; private set; }
    public SpeedTestResult? LatestSpeedTestResult { get; private set; }

    public event EventHandler<(NetworkSnapshot Network, SystemMetricsSnapshot System)>? MetricsUpdated;
    public event EventHandler<AlertNotification>? AlertTriggered;

    public MonitoringOrchestrator(
        INetworkMetricsProvider networkProvider,
        ICpuMetricsProvider cpuProvider,
        IMemoryMetricsProvider memoryProvider,
        IDiskMetricsProvider diskProvider,
        IHardwareSensorProvider sensorProvider,
        IProcessMonitorProvider processProvider,
        INotificationService notificationService,
        ILocalPersistenceService persistenceService,
        ISettingsService settingsService,
        NotificationEvaluationEngine notificationEngine)
    {
        _networkProvider = networkProvider;
        _cpuProvider = cpuProvider;
        _memoryProvider = memoryProvider;
        _diskProvider = diskProvider;
        _sensorProvider = sensorProvider;
        _processProvider = processProvider;
        _notificationService = notificationService;
        _persistenceService = persistenceService;
        _settingsService = settingsService;
        _notificationEngine = notificationEngine;
    }

    public void StartBackgroundMonitoring()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_loopTask is not null && !_loopTask.IsCompleted)
        {
            return;
        }

        _loopCts = new CancellationTokenSource();
        _loopTask = Task.Run(() => RunMonitoringLoopAsync(_loopCts.Token));
    }

    public async Task StopBackgroundMonitoringAsync()
    {
        if (_loopCts is null)
        {
            return;
        }

        try
        {
            await _loopCts.CancelAsync();
            if (_loopTask is not null)
            {
                await _loopTask.WaitAsync(TimeSpan.FromSeconds(3));
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on cancellation
        }
        catch (TimeoutException)
        {
            // Loop did not finish within grace period
        }
        finally
        {
            _loopCts.Dispose();
            _loopCts = null;
            _loopTask = null;
        }
    }

    public async Task<(NetworkSnapshot Network, SystemMetricsSnapshot System)> SampleOnceAsync(
        CancellationToken cancellationToken = default)
    {
        if (!await _sampleLock.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken))
        {
            return (LatestNetworkSnapshot, LatestSystemSnapshot);
        }

        try
        {
            var settings = _settingsService.CurrentSettings;

            var networkTask = _networkProvider.CaptureSnapshotAsync(
                settings.DiagnosticTargets,
                settings.ProbeTimeoutMs,
                cancellationToken);

            var cpuTask = _cpuProvider.GetCpuMetricsAsync(cancellationToken);
            var memTask = _memoryProvider.GetMemoryMetricsAsync(cancellationToken);
            var diskTask = _diskProvider.GetStorageVolumesAsync(cancellationToken);
            var tempTask = _sensorProvider.GetCpuTemperatureAsync(cancellationToken);
            var procTask = _processProvider.GetTopProcessesAsync(
                settings.MaxProcessEntries,
                settings.DefaultProcessSort,
                cancellationToken);

            await Task.WhenAll(networkTask, cpuTask, memTask, diskTask, tempTask, procTask);

            var network = await networkTask;
            var cpu = await cpuTask;
            var mem = await memTask;
            var disks = await diskTask;
            var temp = await tempTask;
            var procs = await procTask;
            var uptime = _processProvider.GetSystemUptime();

            var system = new SystemMetricsSnapshot
            {
                TimestampUtc = DateTimeOffset.UtcNow,
                Hostname = SafeGetHostname(),
                Username = SafeGetUsername(),
                OsDescription = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
                CpuModel = cpu.ModelName,
                LogicalCores = cpu.LogicalCores,
                CpuUsagePercent = cpu.UsagePercent,
                CpuFrequencyMHz = cpu.CurrentFrequencyMHz,
                CpuTemperatureCelsius = temp.TemperatureCelsius,
                TemperatureSensorStatus = temp.Status,
                TemperatureSensorNote = temp.Note,
                TotalRamBytes = mem.TotalBytes,
                UsedRamBytes = mem.UsedBytes,
                AvailableRamBytes = mem.AvailableBytes,
                RamUsagePercent = mem.UsagePercent,
                SystemUptime = uptime,
                Volumes = disks,
                TopProcesses = procs
            };

            LatestNetworkSnapshot = network;
            LatestSystemSnapshot = system;

            var alerts = _notificationEngine.Evaluate(network, system, settings, DateTimeOffset.UtcNow);
            foreach (var alert in alerts)
            {
                try
                {
                    await _persistenceService.SaveAlertAsync(alert, cancellationToken);
                    await _notificationService.SendNotificationAsync(alert, cancellationToken);
                }
                catch
                {
                    // Non-fatal notification or persistence failure
                }

                AlertTriggered?.Invoke(this, alert);
            }

            MetricsUpdated?.Invoke(this, (network, system));
            return (network, system);
        }
        finally
        {
            _sampleLock.Release();
        }
    }

    public void RecordDiagnosis(DiagnosticReport report)
    {
        LatestDiagnosticReport = report;
    }

    public void RecordSpeedTest(SpeedTestResult result)
    {
        LatestSpeedTestResult = result;
    }

    public TechnicalReportDocument BuildReportDocument(
        IReadOnlyList<AlertNotification> recentAlerts,
        PrivacyRedactionOptions privacyOptions)
    {
        var net = LatestNetworkSnapshot;
        var sys = LatestSystemSnapshot;

        var warnings = new List<string>();
        if (net.ConnectionStatus != HealthStatus.Healthy)
        {
            warnings.Add($"Network status is {net.ConnectionStatus}: {net.ConnectionStatusSummary}");
        }

        if (net.PacketLossPercent > 0)
        {
            warnings.Add($"Observed packet loss: {net.PacketLossPercent:F1}%");
        }

        foreach (var vol in sys.Volumes.Where(v => v.IsAccessible && v.UsagePercent >= 90.0))
        {
            warnings.Add($"Volume '{vol.MountPoint}' is at {vol.UsagePercent:F1}% capacity ({vol.AvailableGigabytes:F1} GB remaining).");
        }

        var unavailable = new List<string>();
        if (sys.TemperatureSensorStatus != SensorAvailabilityStatus.Available)
        {
            unavailable.Add($"CPU Temperature ({sys.TemperatureSensorStatus}): {sys.TemperatureSensorNote}");
        }

        if (!sys.CpuFrequencyMHz.HasValue)
        {
            unavailable.Add("CPU Operating Frequency: Operating system did not expose real-time clock speed.");
        }

        foreach (var vol in sys.Volumes.Where(v => !v.IsAccessible))
        {
            unavailable.Add($"Storage Volume '{vol.MountPoint}': {vol.StatusNote ?? "Inaccessible or permission denied."}");
        }

        var rawDoc = new TechnicalReportDocument
        {
            ReportId = Guid.NewGuid(),
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            NateeVersion = "1.0.0",
            System = sys,
            Network = net,
            LatestDiagnosis = LatestDiagnosticReport,
            LatestSpeedTest = LatestSpeedTestResult,
            RecentAlerts = recentAlerts,
            ActiveWarnings = warnings,
            UnavailableMeasurements = unavailable
        };

        return PrivacyRedactionService.ApplyRedaction(rawDoc, privacyOptions);
    }

    private async Task RunMonitoringLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await SampleOnceAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch
            {
                // Keep monitoring loop alive on transient OS query errors
            }

            int intervalSeconds = Math.Clamp(_settingsService.CurrentSettings.MonitoringIntervalSeconds, 1, 60);
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private static string SafeGetHostname()
    {
        try
        {
            return Environment.MachineName;
        }
        catch
        {
            return "Unknown";
        }
    }

    private static string SafeGetUsername()
    {
        try
        {
            return Environment.UserName;
        }
        catch
        {
            return "Unknown";
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            _loopCts?.Cancel();
            _loopCts?.Dispose();
        }
        catch
        {
            // Ignore disposal exceptions
        }

        _sampleLock.Dispose();
    }
}
