using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Natee.App.ViewModels;
using Natee.App.Views;
using Natee.Core.Interfaces;
using Natee.Core.Services;
using Natee.Infrastructure.Network;
using Natee.Infrastructure.Notifications;
using Natee.Infrastructure.Persistence;
using Natee.Infrastructure.Platform;
using Natee.Infrastructure.Reporting;
using Natee.Infrastructure.SystemMetrics;

namespace Natee.App;

public partial class App : Application
{
    private ServiceProvider? _serviceProvider;
    private MainWindow? _mainWindow;
    private MainWindowViewModel? _mainViewModel;
    private bool _isShuttingDown;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            desktop.Exit += OnDesktopExit;

            var services = new ServiceCollection();
            ConfigureServices(services);
            _serviceProvider = services.BuildServiceProvider();

            _mainViewModel = _serviceProvider.GetRequiredService<MainWindowViewModel>();
            DataContext = _mainViewModel;

            _mainWindow = new MainWindow
            {
                DataContext = _mainViewModel
            };
            desktop.MainWindow = _mainWindow;

            bool startMinimized = desktop.Args?.Any(a =>
                string.Equals(a, "--minimized", StringComparison.OrdinalIgnoreCase)) == true;
            bool isSmokeTest = desktop.Args?.Any(a =>
                string.Equals(a, "--smoke-test", StringComparison.OrdinalIgnoreCase)) == true;

            if (!startMinimized)
            {
                _mainWindow.Show();
            }

            _ = InitializeAndRunAsync(isSmokeTest);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private async Task InitializeAndRunAsync(bool isSmokeTest)
    {
        if (_mainViewModel is null || _serviceProvider is null)
        {
            return;
        }

        await _mainViewModel.InitializeAsync();

        if (isSmokeTest)
        {
            // Automated runtime verification mode: measure idle monitoring memory, run full diagnosis + report build, and measure post-diagnostic memory
            var orchestrator = _serviceProvider.GetRequiredService<MonitoringOrchestrator>();
            var (net, sys) = await orchestrator.SampleOnceAsync();

            string sessionType = Environment.GetEnvironmentVariable("XDG_SESSION_TYPE") ?? "unknown";
            string waylandDisplay = Environment.GetEnvironmentVariable("WAYLAND_DISPLAY") ?? "none";
            string x11Display = Environment.GetEnvironmentVariable("DISPLAY") ?? "none";
            string platformImpl = _mainWindow?.PlatformImpl?.GetType().FullName ?? "unknown";

            Console.WriteLine($"[NATEE-SMOKE-TEST] Dashboard & Tray initialized successfully.");
            Console.WriteLine($"[NATEE-SMOKE-TEST] Display Session: XDG_SESSION_TYPE={sessionType} | WAYLAND_DISPLAY={waylandDisplay} | DISPLAY={x11Display} | Avalonia WindowImpl={platformImpl}");
            Console.WriteLine($"[NATEE-SMOKE-TEST] Network Status: {net.ConnectionStatus} ({net.ConnectionStatusSummary})");
            Console.WriteLine($"[NATEE-SMOKE-TEST] Active Interface: {net.ActiveInterfaceName} ({net.ConnectionType}) | IPv4: {net.Ipv4Address ?? "None"}");
            Console.WriteLine($"[NATEE-SMOKE-TEST] CPU: {sys.CpuModel} | Usage: {sys.CpuUsagePercent:F1}% | Freq: {sys.CpuFrequencyMHz?.ToString("F0") ?? "n/a"} MHz | Temp: {sys.CpuTemperatureCelsius?.ToString("F1") ?? "n/a"} C ({sys.TemperatureSensorStatus})");
            Console.WriteLine($"[NATEE-SMOKE-TEST] RAM: {sys.UsedRamGigabytes:F2} GB / {sys.TotalRamGigabytes:F2} GB ({sys.RamUsagePercent:F1}%) | Uptime: {sys.FormattedUptime}");
            Console.WriteLine($"[NATEE-SMOKE-TEST] Volumes: {sys.Volumes.Count} | TopProcesses: {sys.TopProcesses.Count}");

            PrintMemoryBreakdown("Idle Monitoring", forceGc: true);

            await _mainViewModel.RunNetworkDiagnosisAsync();
            _ = orchestrator.BuildReportDocument(Array.Empty<Natee.Core.Models.AlertNotification>(), new Natee.Core.Models.PrivacyRedactionOptions());

            Console.WriteLine($"[NATEE-SMOKE-TEST] Diagnosis Overall: {orchestrator.LatestDiagnosticReport?.OverallStatus} ({orchestrator.LatestDiagnosticReport?.SummaryTitle})");
            PrintMemoryBreakdown("Post-Diagnosis (Before GC)", forceGc: false);
            PrintMemoryBreakdown("Post-Diagnosis (After Compacting GC)", forceGc: true);

            await Task.Delay(300);
            RequestCleanShutdown();
        }
    }

    private static void PrintMemoryBreakdown(string phase, bool forceGc)
    {
        if (forceGc)
        {
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        }

        using var currentProc = System.Diagnostics.Process.GetCurrentProcess();
        double workingSetMb = currentProc.WorkingSet64 / (1024.0 * 1024.0);
        double managedHeapMb = GC.GetTotalMemory(false) / (1024.0 * 1024.0);
        double gcCommittedMb = GC.GetGCMemoryInfo().TotalCommittedBytes / (1024.0 * 1024.0);

        double? rssAnonMb = null;
        double? rssFileMb = null;
        double? pssMb = null;
        double? privateDirtyCleanMb = null;

        if (OperatingSystem.IsLinux())
        {
            try
            {
                foreach (var line in File.ReadLines("/proc/self/status"))
                {
                    if (line.StartsWith("RssAnon:", StringComparison.Ordinal))
                    {
                        rssAnonMb = ParseProcKbToMb(line);
                    }
                    else if (line.StartsWith("RssFile:", StringComparison.Ordinal))
                    {
                        rssFileMb = (rssFileMb ?? 0) + ParseProcKbToMb(line);
                    }
                    else if (line.StartsWith("RssShmem:", StringComparison.Ordinal))
                    {
                        rssFileMb = (rssFileMb ?? 0) + ParseProcKbToMb(line);
                    }
                }

                if (File.Exists("/proc/self/smaps_rollup"))
                {
                    double privDirty = 0;
                    double privClean = 0;
                    foreach (var line in File.ReadLines("/proc/self/smaps_rollup"))
                    {
                        if (line.StartsWith("Pss:", StringComparison.Ordinal))
                        {
                            pssMb = ParseProcKbToMb(line);
                        }
                        else if (line.StartsWith("Private_Dirty:", StringComparison.Ordinal))
                        {
                            privDirty = ParseProcKbToMb(line);
                        }
                        else if (line.StartsWith("Private_Clean:", StringComparison.Ordinal))
                        {
                            privClean = ParseProcKbToMb(line);
                        }
                    }
                    privateDirtyCleanMb = privDirty + privClean;
                }
            }
            catch
            {
                // Ignore procfs read errors
            }
        }

        Console.WriteLine(
            $"[NATEE-SMOKE-TEST] Memory [{phase}]: " +
            $"ManagedHeap={managedHeapMb:F2} MB | " +
            $"GCCommitted={gcCommittedMb:F2} MB | " +
            $"PrivateAnon(RssAnon)={(rssAnonMb.HasValue ? $"{rssAnonMb.Value:F2} MB" : "n/a")} | " +
            $"PrivatePages(Dirty+Clean)={(privateDirtyCleanMb.HasValue ? $"{privateDirtyCleanMb.Value:F2} MB" : "n/a")} | " +
            $"PSS={(pssMb.HasValue ? $"{pssMb.Value:F2} MB" : "n/a")} | " +
            $"SharedFile(RssFile+Shmem)={(rssFileMb.HasValue ? $"{rssFileMb.Value:F2} MB" : "n/a")} | " +
            $"TotalVmRSS={workingSetMb:F2} MB");
    }

    private static double ParseProcKbToMb(string line)
    {
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2 && long.TryParse(parts[1], out long kb))
        {
            return kb / 1024.0;
        }
        return 0;
    }

    private void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<INetworkMetricsProvider, PlatformNetworkMetricsProvider>();
        services.AddSingleton<INetworkDiagnosticsService, NetworkDiagnosticsService>();
        services.AddSingleton<ISpeedTestService>(_ => new CloudflareSpeedTestService());
        services.AddSingleton<ICpuMetricsProvider, PlatformCpuMetricsProvider>();
        services.AddSingleton<IMemoryMetricsProvider, PlatformMemoryMetricsProvider>();
        services.AddSingleton<IDiskMetricsProvider, PlatformDiskMetricsProvider>();
        services.AddSingleton<IHardwareSensorProvider>(_ => new PlatformHardwareSensorProvider());
        services.AddSingleton<IProcessMonitorProvider, PlatformProcessMonitorProvider>();
        services.AddSingleton<INotificationService, DesktopNotificationService>();
        services.AddSingleton<IReportGeneratorService, ReportGeneratorService>();
        services.AddSingleton<ILocalPersistenceService>(_ => new SqlitePersistenceService());
        services.AddSingleton<ISettingsService>(_ => new JsonSettingsService());
        services.AddSingleton<ITicketIntegrationBoundary, LocalTicketAttachmentBoundary>();
        services.AddSingleton<NotificationEvaluationEngine>();
        services.AddSingleton<MonitoringOrchestrator>();

        services.AddSingleton(sp => new MainWindowViewModel(
            sp.GetRequiredService<MonitoringOrchestrator>(),
            sp.GetRequiredService<INetworkDiagnosticsService>(),
            sp.GetRequiredService<ISpeedTestService>(),
            sp.GetRequiredService<IReportGeneratorService>(),
            sp.GetRequiredService<ILocalPersistenceService>(),
            sp.GetRequiredService<ISettingsService>(),
            sp.GetRequiredService<INotificationService>(),
            sp.GetRequiredService<ITicketIntegrationBoundary>(),
            requestApplicationExit: RequestCleanShutdown,
            requestShowMainWindow: ShowAndActivateMainWindow));
    }

    private void OnTrayIconClicked(object? sender, EventArgs e)
    {
        ShowAndActivateMainWindow();
    }

    public void ShowAndActivateMainWindow()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_mainWindow is null)
            {
                return;
            }

            if (!_mainWindow.IsVisible)
            {
                _mainWindow.Show();
            }

            if (_mainWindow.WindowState == WindowState.Minimized)
            {
                _mainWindow.WindowState = WindowState.Normal;
            }

            _mainWindow.Activate();
        });
    }

    public void RequestCleanShutdown()
    {
        if (_isShuttingDown)
        {
            return;
        }

        _isShuttingDown = true;

        _ = Task.Run(async () =>
        {
            try
            {
                if (_serviceProvider is not null)
                {
                    var orchestrator = _serviceProvider.GetService<MonitoringOrchestrator>();
                    if (orchestrator is not null)
                    {
                        await orchestrator.StopBackgroundMonitoringAsync();
                    }
                }
            }
            catch
            {
                // Ignore shutdown cleanup exceptions
            }

            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    if (_mainWindow is not null)
                    {
                        _mainWindow.AllowImmediateClose = true;
                        _mainWindow.Hide();
                    }

                    var orchestrator = _serviceProvider?.GetService<MonitoringOrchestrator>();
                    orchestrator?.Dispose();
                    Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                    Program.ReleaseInstanceGuard();
                }
                catch
                {
                    // Ignore cleanup exceptions
                }

                Environment.Exit(0);
            });
        });
    }

    private void OnDesktopExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
    {
        try
        {
            var orchestrator = _serviceProvider?.GetService<MonitoringOrchestrator>();
            orchestrator?.Dispose();
        }
        catch
        {
            // Ignore final disposal exceptions
        }
    }
}
