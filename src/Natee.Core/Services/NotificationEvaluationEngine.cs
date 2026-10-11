using Natee.Core.Models;

namespace Natee.Core.Services;

/// <summary>
/// Stateful engine that evaluates network and system snapshots against user-configured notification rules,
/// applying hysteresis (consecutive sample thresholds), cooldown periods, and automatic recovery alerts.
/// </summary>
public sealed class NotificationEvaluationEngine
{
    private sealed class ConditionTracker
    {
        public int ConsecutiveBreaches { get; set; }
        public bool IsInAlertState { get; set; }
        public DateTimeOffset? LastAlertSentAtUtc { get; set; }
    }

    private readonly Dictionary<string, ConditionTracker> _trackers = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _sync = new();

    public IReadOnlyList<AlertNotification> Evaluate(
        NetworkSnapshot? network,
        SystemMetricsSnapshot? system,
        NateeSettings settings,
        DateTimeOffset nowUtc)
    {
        var emitted = new List<AlertNotification>();
        int requiredSamples = Math.Max(1, settings.ConsecutiveSamplesForAlert);
        TimeSpan cooldown = TimeSpan.FromSeconds(Math.Max(0, settings.AlertCooldownSeconds));

        lock (_sync)
        {
            if (network is not null)
            {
                // 1. Internet connectivity lost / restored
                if (settings.EnableConnectionAlerts && network.ConnectionStatus != HealthStatus.Inconclusive)
                {
                    bool isOffline = network.ConnectionStatus == HealthStatus.Failed;
                    EvaluateCondition(
                        key: "connection",
                        isBreached: isOffline,
                        requiredSamples: requiredSamples,
                        cooldown: cooldown,
                        nowUtc: nowUtc,
                        alertType: AlertType.InternetConnectionLost,
                        recoveryType: AlertType.InternetConnectionRestored,
                        alertSeverity: HealthStatus.Failed,
                        alertTitle: "Internet Connection Lost",
                        alertMessage: $"Natee detected that Internet connectivity failed ({network.ConnectionStatusSummary}).",
                        recoveryTitle: "Internet Connection Restored",
                        recoveryMessage: $"Connectivity has recovered ({network.ConnectionStatusSummary}).",
                        emitted: emitted);
                }

                // 2. High latency
                if (settings.EnableLatencyAlerts && network.LatencyMs.HasValue)
                {
                    bool highLatency = network.LatencyMs.Value > settings.LatencyThresholdMs;
                    EvaluateCondition(
                        key: "latency",
                        isBreached: highLatency,
                        requiredSamples: requiredSamples,
                        cooldown: cooldown,
                        nowUtc: nowUtc,
                        alertType: AlertType.HighLatency,
                        recoveryType: AlertType.LatencyRecovered,
                        alertSeverity: HealthStatus.Degraded,
                        alertTitle: "High Network Latency",
                        alertMessage: $"Measured latency is {network.LatencyMs.Value:F0} ms (threshold: {settings.LatencyThresholdMs:F0} ms).",
                        recoveryTitle: "Network Latency Normalized",
                        recoveryMessage: $"Latency returned to normal ({network.LatencyMs.Value:F0} ms).",
                        emitted: emitted);
                }

                // 3. Packet loss (only when connection is not completely offline to avoid duplicate spam)
                if (settings.EnablePacketLossAlerts && network.ConnectionStatus != HealthStatus.Failed)
                {
                    bool highLoss = network.PacketLossPercent >= settings.PacketLossThresholdPercent && network.PacketLossPercent > 0;
                    EvaluateCondition(
                        key: "packet_loss",
                        isBreached: highLoss,
                        requiredSamples: requiredSamples,
                        cooldown: cooldown,
                        nowUtc: nowUtc,
                        alertType: AlertType.HighPacketLoss,
                        recoveryType: AlertType.PacketLossRecovered,
                        alertSeverity: HealthStatus.Degraded,
                        alertTitle: "Excessive Packet Loss",
                        alertMessage: $"Observed packet loss of {network.PacketLossPercent:F1}% exceeds threshold ({settings.PacketLossThresholdPercent:F1}%).",
                        recoveryTitle: "Packet Loss Cleared",
                        recoveryMessage: $"Packet loss has returned to {network.PacketLossPercent:F1}%.",
                        emitted: emitted);
                }
            }

            if (system is not null)
            {
                // 4. Low disk space across accessible volumes
                if (settings.EnableDiskSpaceAlerts && system.Volumes.Count > 0)
                {
                    foreach (var volume in system.Volumes.Where(v => v.IsAccessible && v.TotalBytes > 0))
                    {
                        double freePercent = Math.Max(0.0, 100.0 - volume.UsagePercent);
                        bool lowDisk = freePercent < settings.MinFreeDiskPercentThreshold;
                        string key = $"disk:{volume.MountPoint}";

                        EvaluateCondition(
                            key: key,
                            isBreached: lowDisk,
                            requiredSamples: 1, // Disk space is non-transient; alert immediately on first observation
                            cooldown: cooldown,
                            nowUtc: nowUtc,
                            alertType: AlertType.LowDiskSpace,
                            recoveryType: AlertType.DiskSpaceRecovered,
                            alertSeverity: HealthStatus.Degraded,
                            alertTitle: $"Low Disk Space ({volume.VolumeId})",
                            alertMessage: $"Volume '{volume.MountPoint}' has {freePercent:F1}% ({volume.AvailableGigabytes:F1} GB) remaining space (threshold: {settings.MinFreeDiskPercentThreshold:F1}%).",
                            recoveryTitle: $"Disk Space Recovered ({volume.VolumeId})",
                            recoveryMessage: $"Volume '{volume.MountPoint}' now has {freePercent:F1}% ({volume.AvailableGigabytes:F1} GB) free space.",
                            emitted: emitted);
                    }
                }

                // 5. Optional CPU pressure
                if (settings.EnableCpuAlerts)
                {
                    bool highCpu = system.CpuUsagePercent >= settings.CpuThresholdPercent;
                    EvaluateCondition(
                        key: "cpu",
                        isBreached: highCpu,
                        requiredSamples: requiredSamples,
                        cooldown: cooldown,
                        nowUtc: nowUtc,
                        alertType: AlertType.HighCpuPressure,
                        recoveryType: AlertType.CpuPressureRecovered,
                        alertSeverity: HealthStatus.Degraded,
                        alertTitle: "Sustained High CPU Usage",
                        alertMessage: $"CPU utilization is at {system.CpuUsagePercent:F1}% (threshold: {settings.CpuThresholdPercent:F1}%).",
                        recoveryTitle: "CPU Usage Normalized",
                        recoveryMessage: $"CPU utilization dropped to {system.CpuUsagePercent:F1}%.",
                        emitted: emitted);
                }

                // 6. Optional RAM pressure
                if (settings.EnableRamAlerts && system.TotalRamBytes > 0)
                {
                    bool highRam = system.RamUsagePercent >= settings.RamThresholdPercent;
                    EvaluateCondition(
                        key: "ram",
                        isBreached: highRam,
                        requiredSamples: requiredSamples,
                        cooldown: cooldown,
                        nowUtc: nowUtc,
                        alertType: AlertType.HighRamPressure,
                        recoveryType: AlertType.RamPressureRecovered,
                        alertSeverity: HealthStatus.Degraded,
                        alertTitle: "High Memory Usage",
                        alertMessage: $"Physical RAM usage is at {system.RamUsagePercent:F1}% (threshold: {settings.RamThresholdPercent:F1}%).",
                        recoveryTitle: "Memory Usage Normalized",
                        recoveryMessage: $"Physical RAM usage dropped to {system.RamUsagePercent:F1}%.",
                        emitted: emitted);
                }
            }
        }

        return emitted;
    }

    private void EvaluateCondition(
        string key,
        bool isBreached,
        int requiredSamples,
        TimeSpan cooldown,
        DateTimeOffset nowUtc,
        AlertType alertType,
        AlertType recoveryType,
        HealthStatus alertSeverity,
        string alertTitle,
        string alertMessage,
        string recoveryTitle,
        string recoveryMessage,
        List<AlertNotification> emitted)
    {
        if (!_trackers.TryGetValue(key, out var tracker))
        {
            tracker = new ConditionTracker();
            _trackers[key] = tracker;
        }

        if (isBreached)
        {
            tracker.ConsecutiveBreaches++;
            if (tracker.ConsecutiveBreaches >= requiredSamples)
            {
                // Suppress repeated alerts while already in alert state unless cooldown has elapsed AND not yet notified,
                // or if transitioning into alert state after cooldown.
                bool cooldownSatisfied = !tracker.LastAlertSentAtUtc.HasValue ||
                                         (nowUtc - tracker.LastAlertSentAtUtc.Value) >= cooldown;

                if (!tracker.IsInAlertState && cooldownSatisfied)
                {
                    tracker.IsInAlertState = true;
                    tracker.LastAlertSentAtUtc = nowUtc;
                    emitted.Add(new AlertNotification
                    {
                        TimestampUtc = nowUtc,
                        AlertType = alertType,
                        Severity = alertSeverity,
                        Title = alertTitle,
                        Message = alertMessage,
                        IsRecovery = false
                    });
                }
            }
        }
        else
        {
            bool wasInAlertState = tracker.IsInAlertState;
            tracker.ConsecutiveBreaches = 0;
            tracker.IsInAlertState = false;

            if (wasInAlertState)
            {
                emitted.Add(new AlertNotification
                {
                    TimestampUtc = nowUtc,
                    AlertType = recoveryType,
                    Severity = HealthStatus.Healthy,
                    Title = recoveryTitle,
                    Message = recoveryMessage,
                    IsRecovery = true
                });
            }
        }
    }

    public void Reset()
    {
        lock (_sync)
        {
            _trackers.Clear();
        }
    }
}
