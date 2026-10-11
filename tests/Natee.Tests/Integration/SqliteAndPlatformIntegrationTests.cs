using System.Net;
using Natee.Core.Models;
using Natee.Infrastructure.Network;
using Natee.Infrastructure.Persistence;
using Natee.Infrastructure.Platform;
using Natee.Infrastructure.SystemMetrics;
using Xunit;

namespace Natee.Tests.Integration;

[Trait("Category", "Integration")]
public sealed class SqliteAndPlatformIntegrationTests
{
    [Fact]
    public async Task SqlitePersistenceService_AppliesMigrationsSavesQueriesPrunesAndClears()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "natee-sqlite-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        string dbPath = Path.Combine(tempDir, "test_history.db");

        try
        {
            var service = new SqlitePersistenceService(dbPath);
            await service.InitializeAsync();

            // Verify migration version 1 applied and idempotent
            int schemaVersion = await service.GetSchemaVersionAsync();
            Assert.Equal(1, schemaVersion);
            await service.InitializeAsync();
            Assert.Equal(1, await service.GetSchemaVersionAsync());

            // Save a diagnostic report, speed test, and alert
            var diag = new DiagnosticReport
            {
                Id = Guid.NewGuid(),
                StartedAtUtc = DateTimeOffset.UtcNow.AddSeconds(-2),
                CompletedAtUtc = DateTimeOffset.UtcNow,
                OverallStatus = HealthStatus.Healthy,
                SummaryTitle = "All checks passed",
                SummaryExplanation = "Integration test diagnosis",
                Checks = new[]
                {
                    new DiagnosticCheckItem { Id = "interface", Title = "Interface", Status = HealthStatus.Healthy }
                }
            };
            await service.SaveDiagnosticReportAsync(diag);

            var speed = new SpeedTestResult
            {
                TimestampUtc = DateTimeOffset.UtcNow,
                ProviderName = "Test Provider",
                DownloadSpeedMbps = 125.4,
                UploadSpeedMbps = 42.8,
                IsSuccess = true
            };
            await service.SaveSpeedTestResultAsync(speed);

            var oldAlert = new AlertNotification
            {
                Id = Guid.NewGuid(),
                TimestampUtc = DateTimeOffset.UtcNow.AddDays(-30),
                AlertType = AlertType.HighLatency,
                Severity = HealthStatus.Degraded,
                Title = "Old High Latency",
                Message = "Should be pruned"
            };
            var recentAlert = new AlertNotification
            {
                Id = Guid.NewGuid(),
                TimestampUtc = DateTimeOffset.UtcNow,
                AlertType = AlertType.LatencyRecovered,
                Severity = HealthStatus.Healthy,
                Title = "Recent Recovery",
                Message = "Should be retained",
                IsRecovery = true
            };

            await service.SaveAlertAsync(oldAlert);
            await service.SaveAlertAsync(recentAlert);

            var loadedDiags = await service.GetRecentDiagnosticsAsync();
            var loadedSpeeds = await service.GetRecentSpeedTestsAsync();
            var loadedAlerts = await service.GetRecentAlertsAsync();

            Assert.Single(loadedDiags);
            Assert.Equal(diag.Id, loadedDiags[0].Id);
            Assert.Single(loadedSpeeds);
            Assert.Equal(125.4, loadedSpeeds[0].DownloadSpeedMbps);
            Assert.Equal(2, loadedAlerts.Count);

            // Prune records older than 14 days
            await service.PruneExpiredHistoryAsync(14);
            var alertsAfterPrune = await service.GetRecentAlertsAsync();
            Assert.Single(alertsAfterPrune);
            Assert.Equal(recentAlert.Id, alertsAfterPrune[0].Id);

            // Clear all history
            await service.ClearAllHistoryAsync();
            Assert.Empty(await service.GetRecentDiagnosticsAsync());
            Assert.Empty(await service.GetRecentSpeedTestsAsync());
            Assert.Empty(await service.GetRecentAlertsAsync());
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
    public async Task SpeedTestService_RespectsCancellationAndMeasuresTransferAccurately()
    {
        // 1. Verify cancellation token is honored immediately
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var serviceWithCancel = new CloudflareSpeedTestService(new HttpClient(new MockSpeedTestHandler()));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            serviceWithCancel.RunSpeedTestAsync(new NateeSettings(), cancellationToken: cts.Token));

        // 2. Verify controlled HTTP payload transfer calculation
        var service = new CloudflareSpeedTestService(new HttpClient(new MockSpeedTestHandler()));
        var settings = new NateeSettings
        {
            SpeedTestDownloadUrl = "https://127.0.0.1/__down",
            SpeedTestUploadUrl = "https://127.0.0.1/__up",
            SpeedTestUploadBytes = 250_000,
            SpeedTestTimeoutSeconds = 10
        };

        var result = await service.RunSpeedTestAsync(settings);
        Assert.True(result.IsSuccess);
        Assert.Equal(500_000, result.BytesDownloaded);
        Assert.Equal(250_000, result.BytesUploaded);
        Assert.True(result.DownloadSpeedMbps > 0);
        Assert.True(result.UploadSpeedMbps > 0);

        // 3. Verify timeout expiration returns a clean failure result rather than throwing unhandled exception
        var slowService = new CloudflareSpeedTestService(new HttpClient(new SlowMockSpeedTestHandler()));
        var timeoutSettings = settings with { SpeedTestTimeoutSeconds = 5 };
        using var quickCancel = new CancellationTokenSource();
        var timeoutResult = await slowService.RunSpeedTestAsync(timeoutSettings, cancellationToken: quickCancel.Token);
        Assert.False(timeoutResult.IsSuccess);
        Assert.Contains("timed out", timeoutResult.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RealOperatingSystemProviders_ReturnValidNonFabricatedMetrics()
    {
        var cpuProvider = new PlatformCpuMetricsProvider();
        var memProvider = new PlatformMemoryMetricsProvider();
        var diskProvider = new PlatformDiskMetricsProvider();
        var procProvider = new PlatformProcessMonitorProvider();

        var cpu = await cpuProvider.GetCpuMetricsAsync();
        Assert.False(string.IsNullOrWhiteSpace(cpu.ModelName));
        Assert.True(cpu.LogicalCores >= 1);
        Assert.InRange(cpu.UsagePercent, 0.0, 100.0);

        var mem = await memProvider.GetMemoryMetricsAsync();
        Assert.True(mem.TotalBytes > 0);
        Assert.InRange(mem.UsagePercent, 0.0, 100.0);

        var uptime = procProvider.GetSystemUptime();
        Assert.True(uptime > TimeSpan.Zero);

        var procs = await procProvider.GetTopProcessesAsync(5, ProcessSortMode.MemoryDescending);
        Assert.NotEmpty(procs);
        Assert.True(procs.Count <= 5);

        var volumes = await diskProvider.GetStorageVolumesAsync();
        Assert.NotNull(volumes);
    }

    [Fact]
    public void SingleInstanceGuard_PreventsSecondConcurrentInstanceInSameDirectory()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "natee-lock-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var first = new SingleInstanceGuard(tempDir);
            using var second = new SingleInstanceGuard(tempDir);

            Assert.True(first.TryAcquire());
            Assert.False(second.TryAcquire());

            first.Dispose();
            Assert.True(second.TryAcquire());
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    private sealed class MockSpeedTestHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(25, cancellationToken);

            if (request.Method == HttpMethod.Get)
            {
                byte[] payload = new byte[500_000];
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(payload)
                };
            }

            if (request.Content is not null)
            {
                _ = await request.Content.ReadAsByteArrayAsync(cancellationToken);
            }

            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private sealed class SlowMockSpeedTestHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            // Simulate immediate internal timeout (linked token cancellation)
            throw new TaskCanceledException("Simulated HTTP timeout");
        }
    }
}
