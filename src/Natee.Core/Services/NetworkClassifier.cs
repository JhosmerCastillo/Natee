using Natee.Core.Models;

namespace Natee.Core.Services;

/// <summary>
/// Pure domain logic for network throughput, latency, packet loss, connectivity status, and diagnostic root-cause analysis.
/// </summary>
public static class NetworkClassifier
{
    /// <summary>
    /// Calculates observed traffic throughput in Megabits per second (Mbps) from byte counter deltas.
    /// Safely handles counter resets, negative deltas, and zero/sub-millisecond intervals.
    /// </summary>
    public static double CalculateThroughputMbps(long previousBytes, long currentBytes, TimeSpan elapsed)
    {
        if (elapsed.TotalSeconds <= 0.001 || currentBytes <= previousBytes || previousBytes < 0)
        {
            return 0.0;
        }

        long deltaBytes = currentBytes - previousBytes;
        double bitsPerSecond = (deltaBytes * 8.0) / elapsed.TotalSeconds;
        double mbps = bitsPerSecond / 1_000_000.0;
        return Math.Round(mbps, 2);
    }

    /// <summary>
    /// Calculates average latency (ms) and packet loss percentage (0..100) from a set of probe samples.
    /// </summary>
    public static (double? AverageLatencyMs, double PacketLossPercent) CalculateProbeMetrics(
        IReadOnlyList<ProbeSample> samples)
    {
        if (samples is null || samples.Count == 0)
        {
            return (null, 0.0);
        }

        int total = samples.Count;
        var successful = samples.Where(s => s.IsSuccess && s.RoundTripTimeMs.HasValue && s.RoundTripTimeMs.Value >= 0).ToList();
        int failedCount = total - successful.Count;

        double lossPercent = Math.Round((failedCount * 100.0) / total, 1);
        if (successful.Count == 0)
        {
            return (null, 100.0);
        }

        double avgLatency = Math.Round(successful.Average(s => s.RoundTripTimeMs!.Value), 1);
        return (avgLatency, lossPercent);
    }

    /// <summary>
    /// Evaluates overall network connectivity health without falsely declaring the connection down when a single remote target fails.
    /// </summary>
    public static (HealthStatus Status, string Summary) EvaluateConnectivity(
        bool hasActiveInterface,
        bool hasLocalIp,
        IReadOnlyList<ProbeSample> samples,
        double latencyThresholdMs = 120.0,
        double packetLossThresholdPercent = 5.0)
    {
        if (!hasActiveInterface)
        {
            return (HealthStatus.Failed, "Offline — No active network interface detected.");
        }

        if (!hasLocalIp)
        {
            return (HealthStatus.Failed, "No IP configuration — Active interface lacks a valid local address.");
        }

        if (samples is null || samples.Count == 0)
        {
            return (HealthStatus.Inconclusive, "Local network active — Remote connectivity probes have not completed.");
        }

        var (avgLatency, lossPercent) = CalculateProbeMetrics(samples);
        int successCount = samples.Count(s => s.IsSuccess);
        int totalCount = samples.Count;

        if (successCount == 0)
        {
            return (HealthStatus.Failed, "Internet unreachable — All remote diagnostic targets failed to respond.");
        }

        // If at least 2 targets succeeded and only 1 target timed out (e.g. ICMP rate-limiting on a single host),
        // distinguish between a single unresponsive target and widespread packet loss.
        if (successCount < totalCount && totalCount >= 3 && successCount >= totalCount - 1)
        {
            if (avgLatency.HasValue && avgLatency.Value > latencyThresholdMs)
            {
                return (HealthStatus.Degraded, $"Degraded — High latency ({avgLatency.Value:F0} ms) and 1 of {totalCount} targets unresponsive.");
            }

            if (packetLossThresholdPercent <= 34.0)
            {
                return (HealthStatus.Degraded, $"Online (Partial probe loss) — {successCount}/{totalCount} targets reachable ({avgLatency:F0} ms). One remote target did not respond.");
            }

            return (HealthStatus.Healthy, $"Connected — {successCount}/{totalCount} targets reachable ({avgLatency:F0} ms).");
        }

        if (lossPercent >= packetLossThresholdPercent && lossPercent > 0)
        {
            return (HealthStatus.Degraded, $"Degraded — Packet loss at {lossPercent:F1}% ({successCount}/{totalCount} probes succeeded).");
        }

        if (avgLatency.HasValue && avgLatency.Value > latencyThresholdMs)
        {
            return (HealthStatus.Degraded, $"Degraded — Elevated latency ({avgLatency.Value:F0} ms exceeds {latencyThresholdMs:F0} ms threshold).");
        }

        return (HealthStatus.Healthy, $"Healthy — Connected ({avgLatency:F0} ms avg latency, 0% loss).");
    }

    /// <summary>
    /// Synthesizes an evidence-based diagnostic summary from individual one-click diagnostic checks.
    /// </summary>
    public static (HealthStatus OverallStatus, string Title, string Explanation, IReadOnlyList<string> LikelyCauses, string? UncertaintyNotice)
        BuildDiagnosticSummary(IReadOnlyList<DiagnosticCheckItem> checks)
    {
        if (checks is null || checks.Count == 0)
        {
            return (
                HealthStatus.Inconclusive,
                "Diagnosis Inconclusive",
                "No diagnostic checks were completed.",
                Array.Empty<string>(),
                "Diagnostic execution did not produce measurable check items.");
        }

        var byId = checks.ToDictionary(c => c.Id, StringComparer.OrdinalIgnoreCase);
        var causes = new List<string>();
        string? uncertainty = null;

        bool interfaceFailed = byId.TryGetValue("interface", out var ifaceCheck) && ifaceCheck.Status == HealthStatus.Failed;
        bool ipFailed = byId.TryGetValue("ip_config", out var ipCheck) && ipCheck.Status == HealthStatus.Failed;
        bool gatewayFailed = byId.TryGetValue("gateway", out var gwCheck) && gwCheck.Status == HealthStatus.Failed;
        bool gatewayInconclusive = gwCheck?.Status == HealthStatus.Inconclusive;
        bool dnsConfigFailed = byId.TryGetValue("dns_config", out var dnsCfgCheck) && dnsCfgCheck.Status == HealthStatus.Failed;
        bool dnsResolveFailed = byId.TryGetValue("dns_resolution", out var dnsResCheck) && dnsResCheck.Status == HealthStatus.Failed;
        bool internetReachFailed = byId.TryGetValue("internet_reachability", out var netCheck) && netCheck.Status == HealthStatus.Failed;
        bool internetReachHealthy = netCheck?.Status == HealthStatus.Healthy || netCheck?.Status == HealthStatus.Degraded;
        bool latencyDegraded = byId.TryGetValue("latency", out var latCheck) && latCheck.Status == HealthStatus.Degraded;
        bool lossDegraded = byId.TryGetValue("packet_loss", out var lossCheck) && (lossCheck.Status == HealthStatus.Degraded || lossCheck.Status == HealthStatus.Failed);

        if (interfaceFailed)
        {
            causes.Add("Physical or virtual network adapter is disconnected, disabled, or unplugged.");
            return (
                HealthStatus.Failed,
                "Network Interface Offline",
                "No active network adapter with an operational link was found on this computer.",
                causes,
                null);
        }

        if (ipFailed)
        {
            causes.Add("DHCP lease acquisition failed or static IP configuration is missing.");
            return (
                HealthStatus.Failed,
                "Missing IP Configuration",
                "A network adapter is active, but it does not have a valid local IPv4 or IPv6 address.",
                causes,
                null);
        }

        // Case: Internet IP targets are reachable, but DNS name resolution failed!
        if (internetReachHealthy && (dnsConfigFailed || dnsResolveFailed))
        {
            causes.Add("Configured DNS servers are unreachable, misconfigured, or blocking queries.");
            return (
                HealthStatus.Failed,
                "DNS Resolution Failure (IP Connectivity Active)",
                "Direct IP traffic to remote Internet targets succeeded, but domain name resolution failed. Web browsing by hostname will fail until DNS is restored.",
                causes,
                null);
        }

        if (gatewayFailed && internetReachFailed)
        {
            causes.Add("Local router/default gateway is unreachable or down on the local subnet.");
            return (
                HealthStatus.Failed,
                "Local Gateway Unreachable",
                "The computer has a local IP address, but cannot reach the default gateway or external Internet targets.",
                causes,
                "Some routers drop ICMP echo requests by policy; however, external targets also failed to respond.");
        }

        if (internetReachFailed)
        {
            causes.Add("Upstream ISP outage, firewall block, or WAN link disconnection on the router.");
            if (gatewayInconclusive)
            {
                uncertainty = "Default gateway did not respond to probes and all external targets timed out; root cause could be either local router isolation or upstream WAN loss.";
            }

            return (
                HealthStatus.Failed,
                "Internet Connectivity Unavailable",
                "Local interface is configured, but all tested external Internet targets failed to respond.",
                causes,
                uncertainty);
        }

        if (latencyDegraded || lossDegraded)
        {
            if (lossDegraded)
            {
                causes.Add("Packet loss observed across diagnostic probes, indicating link congestion, weak Wi-Fi signal, or ISP routing instability.");
            }

            if (latencyDegraded)
            {
                causes.Add("Round-trip latency exceeds normal thresholds, which may affect real-time voice/video calls.");
            }

            return (
                HealthStatus.Degraded,
                "Connected with Degraded Performance",
                "Internet connectivity and DNS resolution are working, but elevated latency or packet loss was measured.",
                causes,
                "A single snapshot cannot distinguish between local wireless interference and upstream ISP congestion without comparing gateway vs. WAN hops.");
        }

        if (checks.All(c => c.Status is HealthStatus.Inconclusive or HealthStatus.Unsupported))
        {
            return (
                HealthStatus.Inconclusive,
                "Diagnostic Results Inconclusive",
                "The executed checks could not definitively verify network health in the current environment.",
                causes,
                "Operating system or sandbox restrictions prevented standard probe packets from completing.");
        }

        if (checks.Any(c => c.Status == HealthStatus.Degraded))
        {
            causes.Add("One or more individual checks reported minor degradation or partial target timeouts.");
            return (
                HealthStatus.Degraded,
                "Network Operational with Minor Warnings",
                "Core connectivity and DNS resolution succeeded, with minor warnings noted in specific checks.",
                causes,
                null);
        }

        return (
            HealthStatus.Healthy,
            "Network Healthy",
            "Interface configuration, gateway reachability, DNS resolution, latency, and Internet connectivity checks all passed.",
            causes,
            null);
    }
}
