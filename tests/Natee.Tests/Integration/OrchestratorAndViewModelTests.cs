using Natee.App.ViewModels;
using Natee.Core.Interfaces;
using Natee.Core.Models;
using Natee.Core.Services;
using Natee.Infrastructure.Notifications;
using Natee.Infrastructure.Persistence;
using Natee.Infrastructure.Reporting;
using Xunit;

namespace Natee.Tests.Integration;

[Trait("Category", "Integration")]
public sealed class OrchestratorAndViewModelTests
{
    [Fact]
    public async Task MonitoringOrchestrator_SamplesMetricsGeneratesRedactedReportAndStopsCleanly()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "natee-orch-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var settingsService = new JsonSettingsService(Path.Combine(tempDir, "settings.json"));
            await settingsService.LoadAsync();

            var persistence = new SqlitePersistenceService(Path.Combine(tempDir, "history.db"));
            await persistence.InitializeAsync();

            using var orchestrator = new MonitoringOrchestrator(
                new FakeNetworkProvider(),
                new FakeCpuProvider(),
                new FakeMemoryProvider(),
                new FakeDiskProvider(),
                new FakeSensorProvider(),
                new FakeProcessProvider(),
                new DesktopNotificationService(),
                persistence,
                settingsService,
                new NotificationEvaluationEngine());

            var (net, sys) = await orchestrator.SampleOnceAsync();
            Assert.Equal(HealthStatus.Healthy, net.ConnectionStatus);
            Assert.Equal("eth0", net.ActiveInterfaceName);
            Assert.Equal(32.5, sys.CpuUsagePercent);
            Assert.Equal(61.0, sys.CpuTemperatureCelsius);

            // Start and stop background loop cleanly
            orchestrator.StartBackgroundMonitoring();
            await Task.Delay(60);
            await orchestrator.StopBackgroundMonitoringAsync();

            // Build redacted report document
            var alerts = await persistence.GetRecentAlertsAsync();
            var doc = orchestrator.BuildReportDocument(alerts, new PrivacyRedactionOptions
            {
                RedactHostname = true,
                RedactLocalIpAddresses = true
            });

            Assert.Equal(PrivacyRedactionService.RedactedHostnameToken, doc.System.Hostname);
            Assert.Equal(PrivacyRedactionService.RedactedLocalIpToken, doc.Network.Ipv4Address);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task JsonSettingsService_PersistsAndReloadsNormalizedSettings()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "natee-settings-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            string settingsPath = Path.Combine(tempDir, "settings.json");
            var svc = new JsonSettingsService(settingsPath);

            var custom = new NateeSettings
            {
                Theme = ThemePreference.Dark,
                MonitoringIntervalSeconds = 10,
                LatencyThresholdMs = 85.0,
                MaxProcessEntries = 8
            };

            await svc.SaveAsync(custom);
            Assert.True(File.Exists(settingsPath));

            var svc2 = new JsonSettingsService(settingsPath);
            var loaded = await svc2.LoadAsync();

            Assert.Equal(ThemePreference.Dark, loaded.Theme);
            Assert.Equal(10, loaded.MonitoringIntervalSeconds);
            Assert.Equal(85.0, loaded.LatencyThresholdMs);
            Assert.Equal(8, loaded.MaxProcessEntries);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task MainWindowViewModel_ExecutesSettingsAndReportGenerationWorkflows()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "natee-vm-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var settingsService = new JsonSettingsService(Path.Combine(tempDir, "settings.json"));
            var persistence = new SqlitePersistenceService(Path.Combine(tempDir, "history.db"));
            await persistence.InitializeAsync();

            using var orchestrator = new MonitoringOrchestrator(
                new FakeNetworkProvider(),
                new FakeCpuProvider(),
                new FakeMemoryProvider(),
                new FakeDiskProvider(),
                new FakeSensorProvider(),
                new FakeProcessProvider(),
                new DesktopNotificationService(),
                persistence,
                settingsService,
                new NotificationEvaluationEngine());

            await orchestrator.SampleOnceAsync();

            bool exitCalled = false;
            var vm = new MainWindowViewModel(
                orchestrator,
                new FakeDiagnosticsService(),
                new FakeSpeedTestService(),
                new ReportGeneratorService(),
                persistence,
                settingsService,
                new DesktopNotificationService(),
                new LocalTicketAttachmentBoundary(),
                requestApplicationExit: () => exitCalled = true,
                requestShowMainWindow: () => { });

            vm.ReportOutputDirectory = Path.Combine(tempDir, "reports");
            await vm.GenerateReportAsync();

            var generatedFiles = Directory.GetFiles(vm.ReportOutputDirectory);
            Assert.Equal(2, generatedFiles.Length);
            Assert.Contains(generatedFiles, f => f.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(generatedFiles, f => f.EndsWith(".json", StringComparison.OrdinalIgnoreCase));

            vm.DeleteGeneratedReports();
            Assert.Empty(Directory.GetFiles(vm.ReportOutputDirectory));

            vm.ToggleCompactMode();
            Assert.True(vm.IsCompactMode);

            vm.ExitApplication();
            Assert.True(exitCalled);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    private sealed class FakeNetworkProvider : INetworkMetricsProvider
    {
        public Task<NetworkSnapshot> CaptureSnapshotAsync(IReadOnlyList<string> probeTargets, int probeTimeoutMs, CancellationToken cancellationToken = default)
            => Task.FromResult(new NetworkSnapshot
            {
                ConnectionStatus = HealthStatus.Healthy,
                ConnectionStatusSummary = "Healthy",
                ActiveInterfaceId = "eth0",
                ActiveInterfaceName = "eth0",
                ConnectionType = "Ethernet",
                Ipv4Address = "10.0.0.25",
                DefaultGateway = "10.0.0.1",
                DnsServers = new[] { "1.1.1.1" },
                DownloadThroughputMbps = 12.4,
                UploadThroughputMbps = 3.2,
                LatencyMs = 14.0,
                PacketLossPercent = 0.0
            });
    }

    private sealed class FakeCpuProvider : ICpuMetricsProvider
    {
        public Task<(string ModelName, int LogicalCores, double UsagePercent, double? CurrentFrequencyMHz)> GetCpuMetricsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(("Mock CPU", 8, 32.5, (double?)3400.0));
    }

    private sealed class FakeMemoryProvider : IMemoryMetricsProvider
    {
        public Task<(long TotalBytes, long UsedBytes, long AvailableBytes, double UsagePercent)> GetMemoryMetricsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult((16_000_000_000L, 6_000_000_000L, 10_000_000_000L, 37.5));
    }

    private sealed class FakeDiskProvider : IDiskMetricsProvider
    {
        public Task<IReadOnlyList<StorageVolumeMetric>> GetStorageVolumesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<StorageVolumeMetric>>(new[]
            {
                new StorageVolumeMetric
                {
                    VolumeId = "Root (/)",
                    MountPoint = "/",
                    FileSystemType = "ext4",
                    TotalBytes = 250_000_000_000L,
                    UsedBytes = 100_000_000_000L,
                    AvailableBytes = 150_000_000_000L,
                    UsagePercent = 40.0,
                    IsAccessible = true
                }
            });
    }

    private sealed class FakeSensorProvider : IHardwareSensorProvider
    {
        public Task<(double? TemperatureCelsius, SensorAvailabilityStatus Status, string Note)> GetCpuTemperatureAsync(CancellationToken cancellationToken = default)
            => Task.FromResult< (double?, SensorAvailabilityStatus, string)>((61.0, SensorAvailabilityStatus.Available, "coretemp"));
    }

    private sealed class FakeProcessProvider : IProcessMonitorProvider
    {
        public TimeSpan GetSystemUptime() => TimeSpan.FromHours(12);
        public Task<IReadOnlyList<ProcessResourceMetric>> GetTopProcessesAsync(int maxEntries, ProcessSortMode sortMode, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ProcessResourceMetric>>(new[]
            {
                new ProcessResourceMetric { ProcessId = 101, ProcessName = "natee", CpuUsagePercent = 0.4, MemoryBytes = 45_000_000L }
            });
    }

    private sealed class FakeDiagnosticsService : INetworkDiagnosticsService
    {
        public Task<DiagnosticReport> RunDiagnosisAsync(NateeSettings settings, IProgress<string>? stepProgress = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new DiagnosticReport { OverallStatus = HealthStatus.Healthy, SummaryTitle = "Healthy" });
    }

    private sealed class FakeSpeedTestService : ISpeedTestService
    {
        public Task<SpeedTestResult> RunSpeedTestAsync(NateeSettings settings, IProgress<SpeedTestProgress>? progress = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new SpeedTestResult { IsSuccess = true, DownloadSpeedMbps = 100, UploadSpeedMbps = 40 });
    }
}
