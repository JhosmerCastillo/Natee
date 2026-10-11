using Natee.Core.Models;
using Natee.Core.Services;
using Xunit;

namespace Natee.Tests.Unit;

[Trait("Category", "Unit")]
public sealed class NotificationAndSettingsTests
{
    [Fact]
    public void NotificationEngine_RespectsHysteresisCooldownAndRecovery()
    {
        var engine = new NotificationEvaluationEngine();
        var settings = new NateeSettings
        {
            EnableConnectionAlerts = true,
            EnableLatencyAlerts = true,
            LatencyThresholdMs = 100.0,
            ConsecutiveSamplesForAlert = 2,
            AlertCooldownSeconds = 300
        };

        var t0 = DateTimeOffset.UtcNow;

        var highLatencySnap = new NetworkSnapshot
        {
            ConnectionStatus = HealthStatus.Degraded,
            LatencyMs = 180.0
        };

        // Sample 1: First spike -> Hysteresis (requires 2 consecutive samples) suppresses alert
        var alerts1 = engine.Evaluate(highLatencySnap, null, settings, t0);
        Assert.Empty(alerts1);

        // Sample 2: Second consecutive spike -> Emits HighLatency alert
        var alerts2 = engine.Evaluate(highLatencySnap, null, settings, t0.AddSeconds(3));
        Assert.Single(alerts2);
        Assert.Equal(AlertType.HighLatency, alerts2[0].AlertType);
        Assert.False(alerts2[0].IsRecovery);

        // Sample 3: Condition remains unchanged -> Suppressed (anti-spam)
        var alerts3 = engine.Evaluate(highLatencySnap, null, settings, t0.AddSeconds(6));
        Assert.Empty(alerts3);

        // Sample 4: Latency normalizes -> Emits LatencyRecovered notification
        var normalSnap = new NetworkSnapshot
        {
            ConnectionStatus = HealthStatus.Healthy,
            LatencyMs = 22.0
        };
        var alerts4 = engine.Evaluate(normalSnap, null, settings, t0.AddSeconds(9));
        Assert.Single(alerts4);
        Assert.Equal(AlertType.LatencyRecovered, alerts4[0].AlertType);
        Assert.True(alerts4[0].IsRecovery);

        // Sample 5: Immediate re-breach within 300s cooldown -> Suppressed until cooldown expires
        engine.Evaluate(highLatencySnap, null, settings, t0.AddSeconds(12));
        var alertsWithinCooldown = engine.Evaluate(highLatencySnap, null, settings, t0.AddSeconds(15));
        Assert.Empty(alertsWithinCooldown);

        // Sample 6: After 300s cooldown has elapsed -> Alert is permitted again
        var alertsAfterCooldown = engine.Evaluate(highLatencySnap, null, settings, t0.AddSeconds(310));
        Assert.Single(alertsAfterCooldown);
        Assert.Equal(AlertType.HighLatency, alertsAfterCooldown[0].AlertType);
    }

    [Fact]
    public void NotificationEngine_TriggersLowDiskSpaceAndConnectionLostAlerts()
    {
        var engine = new NotificationEvaluationEngine();
        var settings = new NateeSettings
        {
            EnableConnectionAlerts = true,
            EnableDiskSpaceAlerts = true,
            MinFreeDiskPercentThreshold = 10.0,
            ConsecutiveSamplesForAlert = 1
        };

        var offlineNet = new NetworkSnapshot
        {
            ConnectionStatus = HealthStatus.Failed,
            ConnectionStatusSummary = "All targets unreachable"
        };

        var lowDiskSys = new SystemMetricsSnapshot
        {
            Volumes = new[]
            {
                new StorageVolumeMetric
                {
                    VolumeId = "Root (/)",
                    MountPoint = "/",
                    TotalBytes = 100_000_000_000,
                    UsedBytes = 95_000_000_000,
                    AvailableBytes = 5_000_000_000,
                    UsagePercent = 95.0,
                    IsAccessible = true
                }
            }
        };

        var alerts = engine.Evaluate(offlineNet, lowDiskSys, settings, DateTimeOffset.UtcNow);
        Assert.Equal(2, alerts.Count);
        Assert.Contains(alerts, a => a.AlertType == AlertType.InternetConnectionLost);
        Assert.Contains(alerts, a => a.AlertType == AlertType.LowDiskSpace);
    }

    [Fact]
    public void SettingsValidator_ClampsInvalidValuesAndReportsErrors()
    {
        var invalid = new NateeSettings
        {
            MonitoringIntervalSeconds = 0,
            ProbeTimeoutMs = 50,
            LatencyThresholdMs = -10,
            PacketLossThresholdPercent = 250,
            MinFreeDiskPercentThreshold = 0,
            AlertCooldownSeconds = 1,
            ConsecutiveSamplesForAlert = 99,
            MaxProcessEntries = 500,
            HistoryRetentionDays = 365,
            DiagnosticTargets = new List<string>(),
            SpeedTestDownloadUrl = "ftp://invalid-endpoint",
            SpeedTestUploadUrl = "not-a-url"
        };

        var (sanitized, errors) = SettingsValidator.ValidateAndNormalize(invalid);

        Assert.NotEmpty(errors);
        Assert.Equal(1, sanitized.MonitoringIntervalSeconds);
        Assert.Equal(500, sanitized.ProbeTimeoutMs);
        Assert.Equal(10.0, sanitized.LatencyThresholdMs);
        Assert.Equal(100.0, sanitized.PacketLossThresholdPercent);
        Assert.Equal(1.0, sanitized.MinFreeDiskPercentThreshold);
        Assert.Equal(10, sanitized.AlertCooldownSeconds);
        Assert.Equal(10, sanitized.ConsecutiveSamplesForAlert);
        Assert.Equal(25, sanitized.MaxProcessEntries);
        Assert.Equal(90, sanitized.HistoryRetentionDays);
        Assert.NotEmpty(sanitized.DiagnosticTargets);
        Assert.StartsWith("https://", sanitized.SpeedTestDownloadUrl);
        Assert.StartsWith("https://", sanitized.SpeedTestUploadUrl);
    }
}
