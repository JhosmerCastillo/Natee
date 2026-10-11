using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia;
using Avalonia.Styling;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Natee.App.Localization;
using Natee.Core.Interfaces;
using Natee.Core.Models;
using Natee.Core.Services;
using Natee.Infrastructure.Platform;

namespace Natee.App.ViewModels;

/// <summary>
/// Primary view model coordinating the Natee dashboard, compact overview, one-click network diagnosis,
/// manual speed test, system resource views, privacy-redacted report generation, and user settings.
/// </summary>
public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly MonitoringOrchestrator _orchestrator;
    private readonly INetworkDiagnosticsService _diagnosticsService;
    private readonly ISpeedTestService _speedTestService;
    private readonly IReportGeneratorService _reportGenerator;
    private readonly ILocalPersistenceService _persistenceService;
    private readonly ISettingsService _settingsService;
    private readonly INotificationService _notificationService;
    private readonly ITicketIntegrationBoundary _ticketBoundary;
    private readonly Action _requestApplicationExit;
    private readonly Action _requestShowMainWindow;

    private CancellationTokenSource? _diagnosisCts;
    private CancellationTokenSource? _speedTestCts;

    [ObservableProperty]
    private bool _isCompactMode;

    [ObservableProperty]
    private int _selectedTabIndex;

    [ObservableProperty]
    private string _trayTooltipText = "Natee — Initializing calm monitoring...";

    [ObservableProperty]
    private string _statusBannerText = "Initializing network and system sensors...";

    [ObservableProperty]
    private string _connectionStatusLabel = "Inconclusive";

    [ObservableProperty]
    private string _connectionBadgeColor = "#64748B";

    [ObservableProperty]
    private string _connectionSummaryText = "Sampling connectivity...";

    // Network metrics
    [ObservableProperty]
    private string _activeInterfaceDisplay = "Detecting...";

    [ObservableProperty]
    private string _connectionTypeDisplay = "Unknown";

    [ObservableProperty]
    private string _ipv4Display = "None";

    [ObservableProperty]
    private string _ipv6Display = "None";

    [ObservableProperty]
    private string _gatewayDisplay = "None";

    [ObservableProperty]
    private string _dnsServersDisplay = "None";

    [ObservableProperty]
    private string _downloadThroughputDisplay = "0.00 Mbps";

    [ObservableProperty]
    private string _uploadThroughputDisplay = "0.00 Mbps";

    [ObservableProperty]
    private string _latencyDisplay = "—";

    [ObservableProperty]
    private string _packetLossDisplay = "0.0%";

    [ObservableProperty]
    private string _lastCheckTimestampDisplay = "—";

    // System metrics
    [ObservableProperty]
    private string _cpuModelDisplay = "Processor";

    [ObservableProperty]
    private double _cpuUsagePercent;

    [ObservableProperty]
    private string _cpuUsageDisplay = "0.0%";

    [ObservableProperty]
    private string _cpuFrequencyDisplay = "Unavailable";

    [ObservableProperty]
    private string _cpuTemperatureDisplay = "Unavailable";

    [ObservableProperty]
    private string _cpuTemperatureNote = string.Empty;

    [ObservableProperty]
    private double _ramUsagePercent;

    [ObservableProperty]
    private string _ramUsageDisplay = "0.0%";

    [ObservableProperty]
    private string _ramDetailsDisplay = "0.00 GB used / 0.00 GB total (0.00 GB available)";

    [ObservableProperty]
    private string _systemUptimeDisplay = "0h 0m";

    [ObservableProperty]
    private ProcessSortMode _currentProcessSortMode = ProcessSortMode.CpuDescending;

    // One-Click Diagnosis
    [ObservableProperty]
    private bool _isDiagnosisRunning;

    [ObservableProperty]
    private string _diagnosisProgressText = "Click 'Run One-Click Diagnosis' to inspect adapter, gateway, DNS, and Internet health.";

    [ObservableProperty]
    private string _diagnosisOverallTitle = "No Diagnosis Executed Yet";

    [ObservableProperty]
    private string _diagnosisOverallStatusText = "Ready";

    [ObservableProperty]
    private string _diagnosisSummaryExplanation = "Run a diagnosis at any time to identify connectivity bottlenecks without freezing the interface.";

    [ObservableProperty]
    private string _diagnosisLikelyCausesText = string.Empty;

    [ObservableProperty]
    private string _diagnosisUncertaintyText = string.Empty;

    // Speed Test
    [ObservableProperty]
    private bool _isSpeedTestRunning;

    [ObservableProperty]
    private double _speedTestProgressPercent;

    [ObservableProperty]
    private string _speedTestPhaseText = "Ready (Manual execution only — never runs automatically)";

    [ObservableProperty]
    private string _speedTestDownloadDisplay = "— Mbps";

    [ObservableProperty]
    private string _speedTestUploadDisplay = "— Mbps";

    [ObservableProperty]
    private string _speedTestLatencyDisplay = "— ms";

    [ObservableProperty]
    private string _speedTestMetaDisplay = "Provider: Cloudflare Edge HTTPS";

    // Privacy & Report Generation
    [ObservableProperty]
    private bool _redactHostname = true;

    [ObservableProperty]
    private bool _redactUsername = true;

    [ObservableProperty]
    private bool _redactLocalIpAddresses = true;

    [ObservableProperty]
    private bool _redactPublicIpAddresses = true;

    [ObservableProperty]
    private bool _redactMacAddresses = true;

    [ObservableProperty]
    private bool _redactInterfaceIdentifiers;

    [ObservableProperty]
    private bool _redactWifiSsid = true;

    [ObservableProperty]
    private bool _redactProcessNames = true;

    [ObservableProperty]
    private bool _redactLocalFilePaths = true;

    [ObservableProperty]
    private string _reportOutputDirectory = AppDataPaths.GetDefaultReportsDirectory();

    [ObservableProperty]
    private string _lastGeneratedReportStatus = "Configure privacy switches above, then click 'Generate PDF & JSON Report'.";

    [ObservableProperty]
    private string _privacyPreviewSummary = string.Empty;

    // Editable Settings Fields
    [ObservableProperty]
    private int _selectedThemeIndex;

    [ObservableProperty]
    private int _monitoringIntervalSeconds = 3;

    [ObservableProperty]
    private string _diagnosticTargetsCsv = "1.1.1.1, 8.8.8.8, 9.9.9.9";

    [ObservableProperty]
    private string _dnsTestHostname = "cloudflare.com";

    [ObservableProperty]
    private bool _includeRouteTraceInDiagnosis;

    [ObservableProperty]
    private bool _enableConnectionAlerts = true;

    [ObservableProperty]
    private bool _enableLatencyAlerts = true;

    [ObservableProperty]
    private double _latencyThresholdMs = 120.0;

    [ObservableProperty]
    private bool _enablePacketLossAlerts = true;

    [ObservableProperty]
    private double _packetLossThresholdPercent = 5.0;

    [ObservableProperty]
    private bool _enableDiskSpaceAlerts = true;

    [ObservableProperty]
    private double _minFreeDiskPercentThreshold = 10.0;

    [ObservableProperty]
    private bool _enableCpuAlerts;

    [ObservableProperty]
    private double _cpuThresholdPercent = 90.0;

    [ObservableProperty]
    private bool _enableRamAlerts;

    [ObservableProperty]
    private double _ramThresholdPercent = 90.0;

    [ObservableProperty]
    private int _alertCooldownSeconds = 300;

    [ObservableProperty]
    private int _consecutiveSamplesForAlert = 2;

    [ObservableProperty]
    private int _maxProcessEntries = 5;

    [ObservableProperty]
    private int _historyRetentionDays = 14;

    [ObservableProperty]
    private bool _minimizeToTrayOnClose = true;

    [ObservableProperty]
    private bool _startWithSession;

    [ObservableProperty]
    private string _speedTestDownloadUrl = "https://speed.cloudflare.com/__down?bytes=15000000";

    [ObservableProperty]
    private string _speedTestUploadUrl = "https://speed.cloudflare.com/__up";

    [ObservableProperty]
    private string _settingsStatusMessage = string.Empty;

    [ObservableProperty]
    private string _notificationCapabilityNote = string.Empty;

    public ObservableCollection<StorageVolumeMetric> StorageVolumes { get; } = new();
    public ObservableCollection<ProcessResourceMetric> TopProcesses { get; } = new();
    public ObservableCollection<DiagnosticCheckItem> DiagnosticChecks { get; } = new();
    public ObservableCollection<AlertNotification> RecentAlerts { get; } = new();
    public ObservableCollection<DiagnosticReport> DiagnosticHistory { get; } = new();
    public ObservableCollection<SpeedTestResult> SpeedTestHistory { get; } = new();

    public MainWindowViewModel(
        MonitoringOrchestrator orchestrator,
        INetworkDiagnosticsService diagnosticsService,
        ISpeedTestService speedTestService,
        IReportGeneratorService reportGenerator,
        ILocalPersistenceService persistenceService,
        ISettingsService settingsService,
        INotificationService notificationService,
        ITicketIntegrationBoundary ticketBoundary,
        Action requestApplicationExit,
        Action requestShowMainWindow)
    {
        _orchestrator = orchestrator;
        _diagnosticsService = diagnosticsService;
        _speedTestService = speedTestService;
        _reportGenerator = reportGenerator;
        _persistenceService = persistenceService;
        _settingsService = settingsService;
        _notificationService = notificationService;
        _ticketBoundary = ticketBoundary;
        _requestApplicationExit = requestApplicationExit;
        _requestShowMainWindow = requestShowMainWindow;

        _notificationCapabilityNote = _notificationService.CapabilityStatusNote;

        _orchestrator.MetricsUpdated += OnMetricsUpdated;
        _orchestrator.AlertTriggered += OnAlertTriggered;

        LoadSettingsIntoViewModel(_settingsService.CurrentSettings);
        UpdatePrivacyPreview();
    }

    public async Task InitializeAsync()
    {
        try
        {
            await _persistenceService.InitializeAsync();
            var settings = await _settingsService.LoadAsync();
            LoadSettingsIntoViewModel(settings);
            ApplyTheme(settings.Theme);

            await _persistenceService.PruneExpiredHistoryAsync(settings.HistoryRetentionDays);
            await RefreshHistoryCollectionsAsync();

            _orchestrator.StartBackgroundMonitoring();
        }
        catch (Exception ex)
        {
            StatusBannerText = $"Initialization warning: {ex.Message}";
        }
    }

    [RelayCommand]
    public void ToggleCompactMode()
    {
        IsCompactMode = !IsCompactMode;
    }

    [RelayCommand]
    public void OpenDashboardFromTray()
    {
        _requestShowMainWindow();
    }

    [RelayCommand]
    public void OpenSettingsTab()
    {
        _requestShowMainWindow();
        IsCompactMode = false;
        SelectedTabIndex = 6;
    }

    [RelayCommand]
    public void OpenReportsTab()
    {
        _requestShowMainWindow();
        IsCompactMode = false;
        SelectedTabIndex = 4;
    }

    [RelayCommand]
    public void ExitApplication()
    {
        _requestApplicationExit();
    }

    [RelayCommand]
    public async Task SortProcessesByCpuAsync()
    {
        CurrentProcessSortMode = ProcessSortMode.CpuDescending;
        var updated = _settingsService.CurrentSettings with { DefaultProcessSort = ProcessSortMode.CpuDescending };
        await _settingsService.SaveAsync(updated);
        await _orchestrator.SampleOnceAsync();
    }

    [RelayCommand]
    public async Task SortProcessesByMemoryAsync()
    {
        CurrentProcessSortMode = ProcessSortMode.MemoryDescending;
        var updated = _settingsService.CurrentSettings with { DefaultProcessSort = ProcessSortMode.MemoryDescending };
        await _settingsService.SaveAsync(updated);
        await _orchestrator.SampleOnceAsync();
    }

    [RelayCommand]
    public async Task RunNetworkDiagnosisAsync()
    {
        if (IsDiagnosisRunning)
        {
            return;
        }

        _requestShowMainWindow();
        if (!IsCompactMode)
        {
            SelectedTabIndex = 1;
        }

        IsDiagnosisRunning = true;
        _diagnosisCts = new CancellationTokenSource();

        try
        {
            var progress = new Progress<string>(msg =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    DiagnosisProgressText = msg;
                    StatusBannerText = msg;
                });
            });

            var report = await _diagnosticsService.RunDiagnosisAsync(
                _settingsService.CurrentSettings,
                progress,
                _diagnosisCts.Token);

            _orchestrator.RecordDiagnosis(report);
            await _persistenceService.SaveDiagnosticReportAsync(report);

            DiagnosisOverallTitle = report.SummaryTitle;
            DiagnosisOverallStatusText = report.OverallStatus.ToString();
            DiagnosisSummaryExplanation = report.SummaryExplanation;
            DiagnosisLikelyCausesText = report.LikelyCauses.Count > 0
                ? "Likely causes: " + string.Join(" ", report.LikelyCauses)
                : "No fault causes identified.";
            DiagnosisUncertaintyText = report.UncertaintyNotice ?? string.Empty;
            DiagnosisProgressText = $"Completed {report.Checks.Count} checks in {report.Duration.TotalMilliseconds:F0} ms.";
            StatusBannerText = $"Diagnosis complete: {report.SummaryTitle}";

            DiagnosticChecks.Clear();
            foreach (var check in report.Checks)
            {
                DiagnosticChecks.Add(check);
            }

            await RefreshHistoryCollectionsAsync();
        }
        catch (OperationCanceledException)
        {
            DiagnosisProgressText = "Network diagnosis cancelled by user.";
            StatusBannerText = "Network diagnosis cancelled.";
        }
        catch (Exception ex)
        {
            DiagnosisProgressText = $"Diagnosis encountered an error: {ex.Message}";
            StatusBannerText = "Network diagnosis error.";
        }
        finally
        {
            IsDiagnosisRunning = false;
            _diagnosisCts?.Dispose();
            _diagnosisCts = null;
        }
    }

    [RelayCommand]
    public void CancelDiagnosis()
    {
        _diagnosisCts?.Cancel();
    }

    [RelayCommand]
    public async Task RunSpeedTestAsync()
    {
        if (IsSpeedTestRunning)
        {
            return;
        }

        _requestShowMainWindow();
        if (!IsCompactMode)
        {
            SelectedTabIndex = 2;
        }

        IsSpeedTestRunning = true;
        SpeedTestProgressPercent = 0;
        SpeedTestPhaseText = "Starting user-initiated HTTPS speed test...";
        _speedTestCts = new CancellationTokenSource();

        try
        {
            var progress = new Progress<SpeedTestProgress>(p =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    SpeedTestProgressPercent = p.ProgressPercent;
                    SpeedTestPhaseText = p.CurrentMbps > 0
                        ? $"{p.Phase} ({p.CurrentMbps:F2} Mbps)"
                        : p.Phase;
                });
            });

            var result = await _speedTestService.RunSpeedTestAsync(
                _settingsService.CurrentSettings,
                progress,
                _speedTestCts.Token);

            _orchestrator.RecordSpeedTest(result);
            await _persistenceService.SaveSpeedTestResultAsync(result);

            if (result.IsSuccess)
            {
                SpeedTestDownloadDisplay = $"{result.DownloadSpeedMbps:F2} Mbps";
                SpeedTestUploadDisplay = $"{result.UploadSpeedMbps:F2} Mbps";
                SpeedTestLatencyDisplay = result.LatencyMs.HasValue ? $"{result.LatencyMs.Value:F0} ms" : "—";
                SpeedTestMetaDisplay = $"Provider: {result.ProviderName} | Duration: {result.Duration.TotalSeconds:F1}s | Transferred: {(result.BytesDownloaded + result.BytesUploaded) / (1024.0 * 1024.0):F1} MB";
                SpeedTestPhaseText = $"Completed at {result.TimestampUtc.ToLocalTime():HH:mm:ss}. {result.LimitationsNotice}";
                StatusBannerText = $"Speed test finished: Down {result.DownloadSpeedMbps:F1} Mbps / Up {result.UploadSpeedMbps:F1} Mbps";
            }
            else
            {
                SpeedTestPhaseText = result.ErrorMessage ?? "Speed test failed.";
                StatusBannerText = "Speed test could not reach the test server.";
            }

            await RefreshHistoryCollectionsAsync();
        }
        catch (OperationCanceledException)
        {
            SpeedTestPhaseText = "Speed test cancelled by user.";
            StatusBannerText = "Speed test cancelled.";
        }
        catch (Exception ex)
        {
            SpeedTestPhaseText = $"Speed test failed: {ex.Message}";
        }
        finally
        {
            IsSpeedTestRunning = false;
            _speedTestCts?.Dispose();
            _speedTestCts = null;
        }
    }

    [RelayCommand]
    public void CancelSpeedTest()
    {
        _speedTestCts?.Cancel();
    }

    [RelayCommand]
    public async Task GenerateReportAsync()
    {
        _requestShowMainWindow();
        try
        {
            UpdatePrivacyPreview();
            var privacyOptions = BuildCurrentPrivacyOptions();
            var alerts = await _persistenceService.GetRecentAlertsAsync(25);
            var doc = _orchestrator.BuildReportDocument(alerts, privacyOptions);

            string outDir = string.IsNullOrWhiteSpace(ReportOutputDirectory)
                ? AppDataPaths.GetDefaultReportsDirectory()
                : ReportOutputDirectory.Trim();
            Directory.CreateDirectory(outDir);

            string stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss");
            string pdfPath = Path.Combine(outDir, $"natee-report-{stamp}.pdf");
            string jsonPath = Path.Combine(outDir, $"natee-report-{stamp}.json");

            await _reportGenerator.GeneratePdfReportAsync(doc, pdfPath);
            await _reportGenerator.GenerateJsonReportAsync(doc, jsonPath);

            var (_, bundleMsg) = await _ticketBoundary.PrepareTicketAttachmentBundleAsync(pdfPath, jsonPath);
            LastGeneratedReportStatus = bundleMsg;
            StatusBannerText = $"Generated PDF & JSON report in '{outDir}'.";
        }
        catch (Exception ex)
        {
            LastGeneratedReportStatus = $"Failed to generate report: {ex.Message}";
            StatusBannerText = "Report generation failed.";
        }
    }

    [RelayCommand]
    public void OpenReportDirectory()
    {
        try
        {
            string dir = string.IsNullOrWhiteSpace(ReportOutputDirectory)
                ? AppDataPaths.GetDefaultReportsDirectory()
                : ReportOutputDirectory.Trim();
            Directory.CreateDirectory(dir);

            Process.Start(new ProcessStartInfo
            {
                FileName = dir,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            LastGeneratedReportStatus = $"Could not launch file manager automatically ({ex.Message}). Directory path: {ReportOutputDirectory}";
        }
    }

    [RelayCommand]
    public void DeleteGeneratedReports()
    {
        try
        {
            string dir = string.IsNullOrWhiteSpace(ReportOutputDirectory)
                ? AppDataPaths.GetDefaultReportsDirectory()
                : ReportOutputDirectory.Trim();

            if (!Directory.Exists(dir))
            {
                LastGeneratedReportStatus = "No generated report files found to delete.";
                return;
            }

            int deleted = 0;
            foreach (string file in Directory.EnumerateFiles(dir, "natee-report-*.*"))
            {
                if (file.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ||
                    file.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(file);
                    deleted++;
                }
            }

            LastGeneratedReportStatus = $"Deleted {deleted} generated report file(s) from '{dir}'.";
            StatusBannerText = $"Deleted {deleted} generated report file(s).";
        }
        catch (Exception ex)
        {
            LastGeneratedReportStatus = $"Error deleting generated reports: {ex.Message}";
        }
    }

    [RelayCommand]
    public async Task ClearLocalHistoryAsync()
    {
        try
        {
            await _persistenceService.ClearAllHistoryAsync();
            await RefreshHistoryCollectionsAsync();
            StatusBannerText = "Cleared all local diagnostic, speed test, and alert history from SQLite.";
        }
        catch (Exception ex)
        {
            StatusBannerText = $"Failed to clear local history: {ex.Message}";
        }
    }

    [RelayCommand]
    public async Task SaveSettingsAsync()
    {
        try
        {
            var targets = DiagnosticTargetsCsv
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.Trim())
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .ToList();

            var theme = SelectedThemeIndex switch
            {
                1 => ThemePreference.Light,
                2 => ThemePreference.Dark,
                _ => ThemePreference.System
            };

            var candidate = _settingsService.CurrentSettings with
            {
                Theme = theme,
                MonitoringIntervalSeconds = MonitoringIntervalSeconds,
                DiagnosticTargets = targets,
                DnsTestHostname = DnsTestHostname,
                IncludeRouteTraceInDiagnosis = IncludeRouteTraceInDiagnosis,
                EnableConnectionAlerts = EnableConnectionAlerts,
                EnableLatencyAlerts = EnableLatencyAlerts,
                LatencyThresholdMs = LatencyThresholdMs,
                EnablePacketLossAlerts = EnablePacketLossAlerts,
                PacketLossThresholdPercent = PacketLossThresholdPercent,
                EnableDiskSpaceAlerts = EnableDiskSpaceAlerts,
                MinFreeDiskPercentThreshold = MinFreeDiskPercentThreshold,
                EnableCpuAlerts = EnableCpuAlerts,
                CpuThresholdPercent = CpuThresholdPercent,
                EnableRamAlerts = EnableRamAlerts,
                RamThresholdPercent = RamThresholdPercent,
                AlertCooldownSeconds = AlertCooldownSeconds,
                ConsecutiveSamplesForAlert = ConsecutiveSamplesForAlert,
                MaxProcessEntries = MaxProcessEntries,
                HistoryRetentionDays = HistoryRetentionDays,
                MinimizeToTrayOnClose = MinimizeToTrayOnClose,
                StartWithSession = StartWithSession,
                SpeedTestDownloadUrl = SpeedTestDownloadUrl,
                SpeedTestUploadUrl = SpeedTestUploadUrl,
                DefaultPrivacyOptions = BuildCurrentPrivacyOptions()
            };

            var (sanitized, validationErrors) = SettingsValidator.ValidateAndNormalize(candidate);
            await _settingsService.SaveAsync(sanitized);
            LoadSettingsIntoViewModel(sanitized);
            ApplyTheme(sanitized.Theme);

            SessionAutostartManager.TryConfigureAutostart(sanitized.StartWithSession, out string autostartNote);
            await _persistenceService.PruneExpiredHistoryAsync(sanitized.HistoryRetentionDays);

            if (validationErrors.Count > 0)
            {
                SettingsStatusMessage = $"Settings saved with normalized bounds: {string.Join(" ", validationErrors)} ({autostartNote})";
            }
            else
            {
                SettingsStatusMessage = $"Settings saved at {DateTimeOffset.Now:HH:mm:ss}. {autostartNote}";
            }

            StatusBannerText = "Settings updated and persisted.";
        }
        catch (Exception ex)
        {
            SettingsStatusMessage = $"Error saving settings: {ex.Message}";
        }
    }

    [RelayCommand]
    public async Task ResetSettingsToDefaultAsync()
    {
        var defaults = new NateeSettings();
        await _settingsService.SaveAsync(defaults);
        LoadSettingsIntoViewModel(defaults);
        ApplyTheme(defaults.Theme);
        SettingsStatusMessage = "Restored default settings.";
    }

    private void OnMetricsUpdated(object? sender, (NetworkSnapshot Network, SystemMetricsSnapshot System) data)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var net = data.Network;
            var sys = data.System;

            ConnectionStatusLabel = net.ConnectionStatus.ToString();
            ConnectionBadgeColor = net.ConnectionStatus switch
            {
                HealthStatus.Healthy => "#26A69A",
                HealthStatus.Degraded => "#F59E0B",
                HealthStatus.Failed => "#EF4444",
                _ => "#64748B"
            };

            ConnectionSummaryText = net.ConnectionStatusSummary;
            ActiveInterfaceDisplay = string.IsNullOrWhiteSpace(net.ActiveInterfaceName) ? "None" : net.ActiveInterfaceName;
            ConnectionTypeDisplay = net.ConnectionType;
            Ipv4Display = net.Ipv4Address ?? "None";
            Ipv6Display = net.Ipv6Address ?? "None";
            GatewayDisplay = net.DefaultGateway ?? "None";
            DnsServersDisplay = net.DnsServers.Count > 0 ? string.Join(", ", net.DnsServers) : "None";
            DownloadThroughputDisplay = $"{net.DownloadThroughputMbps:F2} Mbps";
            UploadThroughputDisplay = $"{net.UploadThroughputMbps:F2} Mbps";
            LatencyDisplay = net.LatencyMs.HasValue ? $"{net.LatencyMs.Value:F0} ms" : "—";
            PacketLossDisplay = $"{net.PacketLossPercent:F1}%";
            LastCheckTimestampDisplay = net.LatestCheckTimestampUtc.ToLocalTime().ToString("HH:mm:ss");

            string latencyPart = net.LatencyMs.HasValue ? $"{net.LatencyMs.Value:F0} ms" : "n/a";
            TrayTooltipText = $"Natee — {net.ConnectionStatus} | Down {net.DownloadThroughputMbps:F1} / Up {net.UploadThroughputMbps:F1} Mbps | Latency {latencyPart} | CPU {sys.CpuUsagePercent:F0}%";
            StatusBannerText = $"{net.ConnectionStatusSummary} | Last check: {LastCheckTimestampDisplay}";

            CpuModelDisplay = $"{sys.CpuModel} ({sys.LogicalCores} logical cores)";
            CpuUsagePercent = sys.CpuUsagePercent;
            CpuUsageDisplay = $"{sys.CpuUsagePercent:F1}%";
            CpuFrequencyDisplay = sys.CpuFrequencyMHz.HasValue
                ? (sys.CpuFrequencyMHz.Value >= 1000
                    ? $"{sys.CpuFrequencyMHz.Value / 1000.0:F2} GHz ({sys.CpuFrequencyMHz.Value:F0} MHz)"
                    : $"{sys.CpuFrequencyMHz.Value:F0} MHz")
                : "Unavailable";

            CpuTemperatureDisplay = sys.CpuTemperatureCelsius.HasValue
                ? $"{sys.CpuTemperatureCelsius.Value:F1} °C"
                : $"Unavailable ({sys.TemperatureSensorStatus})";
            CpuTemperatureNote = sys.TemperatureSensorNote;

            RamUsagePercent = sys.RamUsagePercent;
            RamUsageDisplay = $"{sys.RamUsagePercent:F1}%";
            RamDetailsDisplay = $"{sys.UsedRamGigabytes:F2} GB used / {sys.TotalRamGigabytes:F2} GB total ({sys.AvailableRamGigabytes:F2} GB available)";
            SystemUptimeDisplay = sys.FormattedUptime;

            SyncCollection(StorageVolumes, sys.Volumes);
            SyncCollection(TopProcesses, sys.TopProcesses);
        });
    }

    private void OnAlertTriggered(object? sender, AlertNotification alert)
    {
        Dispatcher.UIThread.Post(() =>
        {
            RecentAlerts.Insert(0, alert);
            while (RecentAlerts.Count > 50)
            {
                RecentAlerts.RemoveAt(RecentAlerts.Count - 1);
            }
        });
    }

    private async Task RefreshHistoryCollectionsAsync()
    {
        var diags = await _persistenceService.GetRecentDiagnosticsAsync(20);
        var speeds = await _persistenceService.GetRecentSpeedTestsAsync(20);
        var alerts = await _persistenceService.GetRecentAlertsAsync(30);

        Dispatcher.UIThread.Post(() =>
        {
            SyncCollection(DiagnosticHistory, diags);
            SyncCollection(SpeedTestHistory, speeds);
            SyncCollection(RecentAlerts, alerts);
        });
    }

    private void LoadSettingsIntoViewModel(NateeSettings s)
    {
        SelectedThemeIndex = s.Theme switch
        {
            ThemePreference.Light => 1,
            ThemePreference.Dark => 2,
            _ => 0
        };
        MonitoringIntervalSeconds = s.MonitoringIntervalSeconds;
        DiagnosticTargetsCsv = string.Join(", ", s.DiagnosticTargets);
        DnsTestHostname = s.DnsTestHostname;
        IncludeRouteTraceInDiagnosis = s.IncludeRouteTraceInDiagnosis;
        EnableConnectionAlerts = s.EnableConnectionAlerts;
        EnableLatencyAlerts = s.EnableLatencyAlerts;
        LatencyThresholdMs = s.LatencyThresholdMs;
        EnablePacketLossAlerts = s.EnablePacketLossAlerts;
        PacketLossThresholdPercent = s.PacketLossThresholdPercent;
        EnableDiskSpaceAlerts = s.EnableDiskSpaceAlerts;
        MinFreeDiskPercentThreshold = s.MinFreeDiskPercentThreshold;
        EnableCpuAlerts = s.EnableCpuAlerts;
        CpuThresholdPercent = s.CpuThresholdPercent;
        EnableRamAlerts = s.EnableRamAlerts;
        RamThresholdPercent = s.RamThresholdPercent;
        AlertCooldownSeconds = s.AlertCooldownSeconds;
        ConsecutiveSamplesForAlert = s.ConsecutiveSamplesForAlert;
        MaxProcessEntries = s.MaxProcessEntries;
        CurrentProcessSortMode = s.DefaultProcessSort;
        IsCompactMode = s.StartInCompactMode;
        HistoryRetentionDays = s.HistoryRetentionDays;
        MinimizeToTrayOnClose = s.MinimizeToTrayOnClose;
        StartWithSession = s.StartWithSession;
        SpeedTestDownloadUrl = s.SpeedTestDownloadUrl;
        SpeedTestUploadUrl = s.SpeedTestUploadUrl;

        var p = s.DefaultPrivacyOptions;
        RedactHostname = p.RedactHostname;
        RedactUsername = p.RedactUsername;
        RedactLocalIpAddresses = p.RedactLocalIpAddresses;
        RedactPublicIpAddresses = p.RedactPublicIpAddresses;
        RedactMacAddresses = p.RedactMacAddresses;
        RedactInterfaceIdentifiers = p.RedactInterfaceIdentifiers;
        RedactWifiSsid = p.RedactWifiSsid;
        RedactProcessNames = p.RedactProcessNames;
        RedactLocalFilePaths = p.RedactLocalFilePaths;
        UpdatePrivacyPreview();
    }

    private PrivacyRedactionOptions BuildCurrentPrivacyOptions() => new()
    {
        RedactHostname = RedactHostname,
        RedactUsername = RedactUsername,
        RedactLocalIpAddresses = RedactLocalIpAddresses,
        RedactPublicIpAddresses = RedactPublicIpAddresses,
        RedactMacAddresses = RedactMacAddresses,
        RedactInterfaceIdentifiers = RedactInterfaceIdentifiers,
        RedactWifiSsid = RedactWifiSsid,
        RedactProcessNames = RedactProcessNames,
        RedactLocalFilePaths = RedactLocalFilePaths
    };

    partial void OnRedactHostnameChanged(bool value) => UpdatePrivacyPreview();
    partial void OnRedactUsernameChanged(bool value) => UpdatePrivacyPreview();
    partial void OnRedactLocalIpAddressesChanged(bool value) => UpdatePrivacyPreview();
    partial void OnRedactPublicIpAddressesChanged(bool value) => UpdatePrivacyPreview();
    partial void OnRedactMacAddressesChanged(bool value) => UpdatePrivacyPreview();
    partial void OnRedactInterfaceIdentifiersChanged(bool value) => UpdatePrivacyPreview();
    partial void OnRedactWifiSsidChanged(bool value) => UpdatePrivacyPreview();
    partial void OnRedactProcessNamesChanged(bool value) => UpdatePrivacyPreview();
    partial void OnRedactLocalFilePathsChanged(bool value) => UpdatePrivacyPreview();

    private void UpdatePrivacyPreview()
    {
        var summary = PrivacyRedactionService.BuildExcludedFieldsSummary(BuildCurrentPrivacyOptions());
        PrivacyPreviewSummary = summary.Count == 0
            ? "All identifying diagnostic fields will be included in the exported PDF and JSON reports."
            : string.Join("\n• ", summary.Prepend("Active Redactions (applied to both PDF and JSON files):").Skip(1))
                .Insert(0, "• ");
    }

    private static void ApplyTheme(ThemePreference preference)
    {
        if (Application.Current is null)
        {
            return;
        }

        Application.Current.RequestedThemeVariant = preference switch
        {
            ThemePreference.Light => ThemeVariant.Light,
            ThemePreference.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };
    }

    private static void SyncCollection<T>(ObservableCollection<T> target, IReadOnlyList<T> source)
    {
        target.Clear();
        foreach (var item in source)
        {
            target.Add(item);
        }
    }
}
