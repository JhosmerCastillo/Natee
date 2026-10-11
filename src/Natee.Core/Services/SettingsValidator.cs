using Natee.Core.Models;

namespace Natee.Core.Services;

/// <summary>
/// Validates and sanitizes user configuration settings to prevent excessive polling, invalid URLs, or unsafe thresholds.
/// </summary>
public static class SettingsValidator
{
    public static (NateeSettings Sanitized, IReadOnlyList<string> ValidationErrors) ValidateAndNormalize(
        NateeSettings? input)
    {
        if (input is null)
        {
            return (new NateeSettings(), new[] { "Settings object was null; restored defaults." });
        }

        var errors = new List<string>();

        int interval = input.MonitoringIntervalSeconds;
        if (interval < 1 || interval > 60)
        {
            errors.Add("Monitoring interval must be between 1 and 60 seconds.");
            interval = Math.Clamp(interval, 1, 60);
        }

        int probeTimeout = input.ProbeTimeoutMs;
        if (probeTimeout < 500 || probeTimeout > 10000)
        {
            errors.Add("Probe timeout must be between 500 and 10000 milliseconds.");
            probeTimeout = Math.Clamp(probeTimeout, 500, 10000);
        }

        double latencyThreshold = input.LatencyThresholdMs;
        if (latencyThreshold < 10.0 || latencyThreshold > 5000.0 || double.IsNaN(latencyThreshold))
        {
            errors.Add("Latency threshold must be between 10 and 5000 ms.");
            latencyThreshold = double.IsNaN(latencyThreshold) ? 120.0 : Math.Clamp(latencyThreshold, 10.0, 5000.0);
        }

        double lossThreshold = input.PacketLossThresholdPercent;
        if (lossThreshold < 0.5 || lossThreshold > 100.0 || double.IsNaN(lossThreshold))
        {
            errors.Add("Packet loss threshold must be between 0.5% and 100%.");
            lossThreshold = double.IsNaN(lossThreshold) ? 5.0 : Math.Clamp(lossThreshold, 0.5, 100.0);
        }

        double diskThreshold = input.MinFreeDiskPercentThreshold;
        if (diskThreshold < 1.0 || diskThreshold > 50.0 || double.IsNaN(diskThreshold))
        {
            errors.Add("Minimum free disk space threshold must be between 1% and 50%.");
            diskThreshold = double.IsNaN(diskThreshold) ? 10.0 : Math.Clamp(diskThreshold, 1.0, 50.0);
        }

        double cpuThreshold = input.CpuThresholdPercent;
        if (cpuThreshold < 20.0 || cpuThreshold > 100.0 || double.IsNaN(cpuThreshold))
        {
            errors.Add("CPU alert threshold must be between 20% and 100%.");
            cpuThreshold = double.IsNaN(cpuThreshold) ? 90.0 : Math.Clamp(cpuThreshold, 20.0, 100.0);
        }

        double ramThreshold = input.RamThresholdPercent;
        if (ramThreshold < 20.0 || ramThreshold > 100.0 || double.IsNaN(ramThreshold))
        {
            errors.Add("RAM alert threshold must be between 20% and 100%.");
            ramThreshold = double.IsNaN(ramThreshold) ? 90.0 : Math.Clamp(ramThreshold, 20.0, 100.0);
        }

        int cooldown = input.AlertCooldownSeconds;
        if (cooldown < 10 || cooldown > 86400)
        {
            errors.Add("Alert cooldown must be between 10 and 86400 seconds.");
            cooldown = Math.Clamp(cooldown, 10, 86400);
        }

        int consecutiveSamples = input.ConsecutiveSamplesForAlert;
        if (consecutiveSamples < 1 || consecutiveSamples > 10)
        {
            errors.Add("Consecutive samples for alert must be between 1 and 10.");
            consecutiveSamples = Math.Clamp(consecutiveSamples, 1, 10);
        }

        int maxProcesses = input.MaxProcessEntries;
        if (maxProcesses < 1 || maxProcesses > 25)
        {
            errors.Add("Maximum displayed processes must be between 1 and 25.");
            maxProcesses = Math.Clamp(maxProcesses, 1, 25);
        }

        int retentionDays = input.HistoryRetentionDays;
        if (retentionDays < 1 || retentionDays > 90)
        {
            errors.Add("History retention must be between 1 and 90 days.");
            retentionDays = Math.Clamp(retentionDays, 1, 90);
        }

        var targets = (input.DiagnosticTargets ?? new List<string>())
            .Select(t => t?.Trim() ?? string.Empty)
            .Where(t => !string.IsNullOrWhiteSpace(t) && t.Length <= 253)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .ToList();

        if (targets.Count == 0)
        {
            errors.Add("At least one diagnostic target is required; restored default targets.");
            targets = new List<string> { "1.1.1.1", "8.8.8.8", "9.9.9.9" };
        }

        string dnsHost = string.IsNullOrWhiteSpace(input.DnsTestHostname)
            ? "cloudflare.com"
            : input.DnsTestHostname.Trim();

        string downloadUrl = input.SpeedTestDownloadUrl?.Trim() ?? string.Empty;
        if (!Uri.TryCreate(downloadUrl, UriKind.Absolute, out var dlUri) ||
            (dlUri.Scheme != Uri.UriSchemeHttps && dlUri.Scheme != Uri.UriSchemeHttp))
        {
            errors.Add("Speed test download URL must be a valid HTTP or HTTPS URI.");
            downloadUrl = "https://speed.cloudflare.com/__down?bytes=15000000";
        }

        string uploadUrl = input.SpeedTestUploadUrl?.Trim() ?? string.Empty;
        if (!Uri.TryCreate(uploadUrl, UriKind.Absolute, out var ulUri) ||
            (ulUri.Scheme != Uri.UriSchemeHttps && ulUri.Scheme != Uri.UriSchemeHttp))
        {
            errors.Add("Speed test upload URL must be a valid HTTP or HTTPS URI.");
            uploadUrl = "https://speed.cloudflare.com/__up";
        }

        int uploadBytes = input.SpeedTestUploadBytes;
        if (uploadBytes < 100_000 || uploadBytes > 50_000_000)
        {
            errors.Add("Speed test upload payload size must be between 100 KB and 50 MB.");
            uploadBytes = Math.Clamp(uploadBytes, 100_000, 50_000_000);
        }

        int speedTimeout = input.SpeedTestTimeoutSeconds;
        if (speedTimeout < 5 || speedTimeout > 120)
        {
            errors.Add("Speed test timeout must be between 5 and 120 seconds.");
            speedTimeout = Math.Clamp(speedTimeout, 5, 120);
        }

        var sanitized = input with
        {
            MonitoringIntervalSeconds = interval,
            ProbeTimeoutMs = probeTimeout,
            LatencyThresholdMs = latencyThreshold,
            PacketLossThresholdPercent = lossThreshold,
            MinFreeDiskPercentThreshold = diskThreshold,
            CpuThresholdPercent = cpuThreshold,
            RamThresholdPercent = ramThreshold,
            AlertCooldownSeconds = cooldown,
            ConsecutiveSamplesForAlert = consecutiveSamples,
            MaxProcessEntries = maxProcesses,
            HistoryRetentionDays = retentionDays,
            DiagnosticTargets = targets,
            DnsTestHostname = dnsHost,
            SpeedTestDownloadUrl = downloadUrl,
            SpeedTestUploadUrl = uploadUrl,
            SpeedTestUploadBytes = uploadBytes,
            SpeedTestTimeoutSeconds = speedTimeout,
            DefaultPrivacyOptions = input.DefaultPrivacyOptions ?? new PrivacyRedactionOptions()
        };

        return (sanitized, errors);
    }
}
