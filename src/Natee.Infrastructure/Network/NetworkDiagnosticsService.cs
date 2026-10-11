using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using Natee.Core.Interfaces;
using Natee.Core.Models;
using Natee.Core.Services;

namespace Natee.Infrastructure.Network;

/// <summary>
/// Executes the One-Click Network Diagnosis workflow, inspecting local adapter state, IP configuration,
/// default gateway reachability, DNS configuration and resolution, multi-target reachability, latency,
/// packet loss, and optional route tracing.
/// </summary>
public sealed class NetworkDiagnosticsService : INetworkDiagnosticsService
{
    private readonly INetworkMetricsProvider _networkProvider;

    public NetworkDiagnosticsService(INetworkMetricsProvider networkProvider)
    {
        _networkProvider = networkProvider;
    }

    public async Task<DiagnosticReport> RunDiagnosisAsync(
        NateeSettings settings,
        IProgress<string>? stepProgress = null,
        CancellationToken cancellationToken = default)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var checks = new List<DiagnosticCheckItem>();

        stepProgress?.Report("Inspecting network interface and IP configuration...");
        var snapshot = await _networkProvider.CaptureSnapshotAsync(
            settings.DiagnosticTargets,
            settings.ProbeTimeoutMs,
            cancellationToken);

        // 1. Network Interface Status
        bool hasInterface = !string.IsNullOrWhiteSpace(snapshot.ActiveInterfaceName) &&
                            !string.Equals(snapshot.ActiveInterfaceName, "None", StringComparison.OrdinalIgnoreCase);

        checks.Add(new DiagnosticCheckItem
        {
            Id = "interface",
            Title = "Network Interface Status",
            Category = "Local Adapter",
            Status = hasInterface ? HealthStatus.Healthy : HealthStatus.Failed,
            WhatWasTested = "Verified whether an operational physical or virtual network adapter is active.",
            MeasuredResult = hasInterface
                ? $"{snapshot.ActiveInterfaceName} ({snapshot.ConnectionType}) is Up"
                : "No active network interface found",
            WhyItMatters = "Without an operational network interface, the system cannot communicate with the local router or the Internet.",
            TechnicalDetails = hasInterface
                ? $"Adapter ID: {snapshot.ActiveInterfaceId} | Type: {snapshot.ConnectionType} | MAC: {snapshot.MacAddress ?? "Unavailable"}"
                : "NetworkInterface.GetAllNetworkInterfaces() returned no operational non-loopback adapters.",
            DurationMs = 5
        });

        // 2. IP Configuration
        bool hasIpv4 = !string.IsNullOrWhiteSpace(snapshot.Ipv4Address);
        bool hasIpv6 = !string.IsNullOrWhiteSpace(snapshot.Ipv6Address);
        bool isApipa = snapshot.Ipv4Address?.StartsWith("169.254.", StringComparison.Ordinal) == true;
        HealthStatus ipStatus = (hasIpv4 && !isApipa) || hasIpv6
            ? HealthStatus.Healthy
            : (isApipa ? HealthStatus.Failed : HealthStatus.Failed);

        checks.Add(new DiagnosticCheckItem
        {
            Id = "ip_config",
            Title = "IP Configuration",
            Category = "Local Adapter",
            Status = hasInterface ? ipStatus : HealthStatus.Failed,
            WhatWasTested = "Checked for valid local IPv4 and IPv6 unicast address assignments.",
            MeasuredResult = hasIpv4 || hasIpv6
                ? $"IPv4: {snapshot.Ipv4Address ?? "None"}, IPv6: {snapshot.Ipv6Address ?? "None"}"
                : "No IP address assigned",
            WhyItMatters = "A valid IP address is required to route packets on the local network and reach the default gateway.",
            TechnicalDetails = isApipa
                ? "Detected link-local APIPA address (169.254.x.x), indicating DHCP server could not be reached."
                : $"Unicast IPv4: {snapshot.Ipv4Address ?? "None"} | Unicast IPv6: {snapshot.Ipv6Address ?? "None"}",
            DurationMs = 2
        });

        // 3. Default Gateway Reachability
        stepProgress?.Report("Checking default gateway reachability...");
        var gwSw = Stopwatch.StartNew();
        if (string.IsNullOrWhiteSpace(snapshot.DefaultGateway))
        {
            gwSw.Stop();
            checks.Add(new DiagnosticCheckItem
            {
                Id = "gateway",
                Title = "Default Gateway Reachability",
                Category = "Local Network",
                Status = hasInterface ? HealthStatus.Failed : HealthStatus.Failed,
                WhatWasTested = "Checked default gateway configuration and local router reachability.",
                MeasuredResult = "No default gateway configured",
                WhyItMatters = "The default gateway forwards traffic from the local subnet to external networks and the Internet.",
                TechnicalDetails = "Routing table does not contain an active default route (0.0.0.0/0).",
                DurationMs = gwSw.Elapsed.TotalMilliseconds
            });
        }
        else
        {
            var gwProbe = await PlatformNetworkMetricsProvider.ProbeHostAsync(
                snapshot.DefaultGateway,
                settings.ProbeTimeoutMs,
                cancellationToken);
            gwSw.Stop();

            checks.Add(new DiagnosticCheckItem
            {
                Id = "gateway",
                Title = "Default Gateway Reachability",
                Category = "Local Network",
                Status = gwProbe.IsSuccess ? HealthStatus.Healthy : HealthStatus.Inconclusive,
                WhatWasTested = $"Probed default gateway at {snapshot.DefaultGateway}.",
                MeasuredResult = gwProbe.IsSuccess
                    ? $"Reachable in {gwProbe.RoundTripTimeMs:F1} ms ({snapshot.DefaultGateway})"
                    : $"Gateway {snapshot.DefaultGateway} did not respond to direct probe",
                WhyItMatters = "Confirms local link health between this computer and the local router before traffic leaves the premises.",
                TechnicalDetails = gwProbe.IsSuccess
                    ? $"Gateway {snapshot.DefaultGateway} responded in {gwProbe.RoundTripTimeMs:F1} ms."
                    : $"Gateway {snapshot.DefaultGateway} timed out. Note: some corporate gateways block direct ICMP/TCP probes while still forwarding traffic.",
                DurationMs = gwSw.Elapsed.TotalMilliseconds
            });
        }

        // 4. DNS Configuration
        bool hasDns = snapshot.DnsServers.Count > 0;
        checks.Add(new DiagnosticCheckItem
        {
            Id = "dns_config",
            Title = "DNS Configuration",
            Category = "Name Resolution",
            Status = hasDns ? HealthStatus.Healthy : HealthStatus.Failed,
            WhatWasTested = "Checked operating-system DNS resolver configuration.",
            MeasuredResult = hasDns
                ? $"{snapshot.DnsServers.Count} DNS server(s): {string.Join(", ", snapshot.DnsServers)}"
                : "No DNS servers configured",
            WhyItMatters = "DNS resolvers translate human-readable domain names into IP addresses.",
            TechnicalDetails = hasDns
                ? $"Configured resolvers: {string.Join(", ", snapshot.DnsServers)}"
                : "No DNS servers found in adapter properties or /etc/resolv.conf.",
            DurationMs = 1
        });

        // 5. DNS Name Resolution
        stepProgress?.Report($"Testing DNS resolution for {settings.DnsTestHostname}...");
        var dnsSw = Stopwatch.StartNew();
        try
        {
            using var dnsCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            dnsCts.CancelAfter(settings.ProbeTimeoutMs);
            IPAddress[] addresses = await Dns.GetHostAddressesAsync(settings.DnsTestHostname, dnsCts.Token);
            dnsSw.Stop();

            bool resolved = addresses.Length > 0;
            checks.Add(new DiagnosticCheckItem
            {
                Id = "dns_resolution",
                Title = "DNS Name Resolution",
                Category = "Name Resolution",
                Status = resolved ? HealthStatus.Healthy : HealthStatus.Failed,
                WhatWasTested = $"Resolved domain name '{settings.DnsTestHostname}' via system DNS.",
                MeasuredResult = resolved
                    ? $"Resolved {addresses.Length} address(es) in {dnsSw.Elapsed.TotalMilliseconds:F0} ms"
                    : "Resolution returned no addresses",
                WhyItMatters = "Verifies that applications and browsers can translate domain names into IP addresses.",
                TechnicalDetails = resolved
                    ? $"Host '{settings.DnsTestHostname}' -> {string.Join(", ", addresses.Take(4).Select(a => a.ToString()))} ({dnsSw.Elapsed.TotalMilliseconds:F1} ms)"
                    : $"Dns.GetHostAddressesAsync('{settings.DnsTestHostname}') returned empty array.",
                DurationMs = dnsSw.Elapsed.TotalMilliseconds
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            dnsSw.Stop();
            checks.Add(new DiagnosticCheckItem
            {
                Id = "dns_resolution",
                Title = "DNS Name Resolution",
                Category = "Name Resolution",
                Status = HealthStatus.Failed,
                WhatWasTested = $"Attempted to resolve '{settings.DnsTestHostname}' via system DNS.",
                MeasuredResult = $"DNS lookup failed ({ex.GetType().Name})",
                WhyItMatters = "When DNS resolution fails, websites and cloud services cannot be reached by name even if raw IP connectivity works.",
                TechnicalDetails = $"Error resolving '{settings.DnsTestHostname}': {ex.Message}",
                DurationMs = dnsSw.Elapsed.TotalMilliseconds
            });
        }

        // 6, 7, 8, 9. Multi-Target Reachability, Latency, and Packet Loss (2 rounds per target for accurate loss sampling)
        var targets = (settings.DiagnosticTargets is { Count: > 0 }
            ? settings.DiagnosticTargets
            : new List<string> { "1.1.1.1", "8.8.8.8", "9.9.9.9" }).Take(5).ToList();

        stepProgress?.Report($"Probing diagnostic targets ({string.Join(", ", targets)})...");
        var probeSw = Stopwatch.StartNew();
        var allSamples = new List<ProbeSample>();

        for (int round = 0; round < 2; round++)
        {
            var roundSamples = await Task.WhenAll(
                targets.Select(t => PlatformNetworkMetricsProvider.ProbeHostAsync(t, settings.ProbeTimeoutMs, cancellationToken)));
            allSamples.AddRange(roundSamples);
        }
        probeSw.Stop();

        var perTargetStatus = targets.Select(t =>
        {
            var targetSamples = allSamples.Where(s => s.Target == t).ToList();
            int ok = targetSamples.Count(s => s.IsSuccess);
            double? rtt = ok > 0 ? targetSamples.Where(s => s.IsSuccess && s.RoundTripTimeMs.HasValue).Average(s => s.RoundTripTimeMs!.Value) : null;
            return (Target: t, Succeeded: ok, Total: targetSamples.Count, AvgRtt: rtt);
        }).ToList();

        int reachableTargets = perTargetStatus.Count(x => x.Succeeded > 0);
        HealthStatus targetStatus = reachableTargets == targets.Count
            ? HealthStatus.Healthy
            : (reachableTargets > 0 ? HealthStatus.Degraded : HealthStatus.Failed);

        checks.Add(new DiagnosticCheckItem
        {
            Id = "target_reachability",
            Title = "Diagnostic Target Reachability",
            Category = "WAN & Internet",
            Status = targetStatus,
            WhatWasTested = $"Probed {targets.Count} configured external target(s): {string.Join(", ", targets)}.",
            MeasuredResult = $"{reachableTargets} of {targets.Count} targets responded",
            WhyItMatters = "Testing multiple independent targets prevents false offline alarms when a single server blocks ICMP or experiences downtime.",
            TechnicalDetails = string.Join(" | ", perTargetStatus.Select(x =>
                x.AvgRtt.HasValue ? $"{x.Target}: {x.Succeeded}/{x.Total} OK ({x.AvgRtt:F0} ms)" : $"{x.Target}: {x.Succeeded}/{x.Total} Failed")),
            DurationMs = probeSw.Elapsed.TotalMilliseconds
        });

        var (avgLatency, lossPercent) = NetworkClassifier.CalculateProbeMetrics(allSamples);

        // 7. Latency check
        HealthStatus latencyStatus = !avgLatency.HasValue
            ? HealthStatus.Failed
            : (avgLatency.Value > settings.LatencyThresholdMs ? HealthStatus.Degraded : HealthStatus.Healthy);

        checks.Add(new DiagnosticCheckItem
        {
            Id = "latency",
            Title = "Network Latency",
            Category = "Performance",
            Status = latencyStatus,
            WhatWasTested = $"Measured round-trip time across {allSamples.Count} probes (threshold: {settings.LatencyThresholdMs:F0} ms).",
            MeasuredResult = avgLatency.HasValue ? $"{avgLatency.Value:F1} ms average" : "No responses received",
            WhyItMatters = "Low latency keeps interactive sessions, voice/video calls, and DNS lookups responsive.",
            TechnicalDetails = avgLatency.HasValue
                ? $"Average RTT: {avgLatency.Value:F1} ms | Threshold: {settings.LatencyThresholdMs:F0} ms"
                : "All probe packets timed out; latency could not be calculated.",
            DurationMs = probeSw.Elapsed.TotalMilliseconds
        });

        // 8. Packet Loss check
        HealthStatus lossStatus = lossPercent >= 100.0
            ? HealthStatus.Failed
            : (lossPercent >= settings.PacketLossThresholdPercent ? HealthStatus.Degraded : HealthStatus.Healthy);

        checks.Add(new DiagnosticCheckItem
        {
            Id = "packet_loss",
            Title = "Packet Loss",
            Category = "Performance",
            Status = lossStatus,
            WhatWasTested = $"Calculated packet loss across {allSamples.Count} probe packets (threshold: {settings.PacketLossThresholdPercent:F1}%).",
            MeasuredResult = $"{lossPercent:F1}% packet loss ({allSamples.Count(s => s.IsSuccess)}/{allSamples.Count} received)",
            WhyItMatters = "Packet loss causes TCP retransmissions, reduced throughput, and audio/video dropouts.",
            TechnicalDetails = $"Total sent: {allSamples.Count} | Received: {allSamples.Count(s => s.IsSuccess)} | Lost: {allSamples.Count(s => !s.IsSuccess)}",
            DurationMs = probeSw.Elapsed.TotalMilliseconds
        });

        // 9. Internet Reachability synthesis check
        HealthStatus internetStatus = reachableTargets > 0
            ? (reachableTargets == targets.Count ? HealthStatus.Healthy : HealthStatus.Degraded)
            : HealthStatus.Failed;

        checks.Add(new DiagnosticCheckItem
        {
            Id = "internet_reachability",
            Title = "Internet Reachability",
            Category = "WAN & Internet",
            Status = internetStatus,
            WhatWasTested = "Verified end-to-end IP packet transport to public Internet endpoints.",
            MeasuredResult = reachableTargets > 0
                ? $"Online ({reachableTargets}/{targets.Count} external endpoints reachable)"
                : "Offline (0 external endpoints reachable)",
            WhyItMatters = "Confirms whether IP traffic can traverse the local gateway and ISP network to reach the public Internet.",
            TechnicalDetails = $"Reachable endpoints: {reachableTargets}/{targets.Count}. Single unresponsive targets do not mark the system offline.",
            DurationMs = 1
        });

        // 10. Optional Route Tracing
        if (settings.IncludeRouteTraceInDiagnosis)
        {
            stepProgress?.Report("Running bounded route trace...");
            var traceCheck = await RunBoundedRouteTraceAsync(targets.First(), cancellationToken);
            checks.Add(traceCheck);
        }
        else
        {
            checks.Add(new DiagnosticCheckItem
            {
                Id = "route_trace",
                Title = "Route Tracing (Optional)",
                Category = "WAN & Internet",
                Status = HealthStatus.Unsupported,
                WhatWasTested = "Optional multi-hop route trace to the primary diagnostic target.",
                MeasuredResult = "Skipped (disabled in Settings)",
                WhyItMatters = "Route tracing identifies the specific hop where packets are delayed or dropped, but can be enabled on demand in Settings.",
                TechnicalDetails = "Enable 'Include route trace in diagnosis' in Settings to execute a 5-hop path trace.",
                DurationMs = 0
            });
        }

        var (overallStatus, summaryTitle, summaryExplanation, likelyCauses, uncertaintyNotice) =
            NetworkClassifier.BuildDiagnosticSummary(checks);

        return new DiagnosticReport
        {
            Id = Guid.NewGuid(),
            StartedAtUtc = startedAt,
            CompletedAtUtc = DateTimeOffset.UtcNow,
            OverallStatus = overallStatus,
            SummaryTitle = summaryTitle,
            SummaryExplanation = summaryExplanation,
            LikelyCauses = likelyCauses,
            UncertaintyNotice = uncertaintyNotice,
            Checks = checks
        };
    }

    private static async Task<DiagnosticCheckItem> RunBoundedRouteTraceAsync(
        string target,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var hops = new List<string>();

        try
        {
            using var ping = new Ping();
            byte[] buffer = new byte[32];

            for (int ttl = 1; ttl <= 5; ttl++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var options = new PingOptions(ttl, true);
                var reply = await ping.SendPingAsync(target, TimeSpan.FromMilliseconds(900), buffer, options, cancellationToken);
                if (reply.Status is IPStatus.Success or IPStatus.TtlExpired)
                {
                    hops.Add($"Hop {ttl}: {reply.Address} ({reply.RoundtripTime} ms)");
                    if (reply.Status == IPStatus.Success)
                    {
                        break;
                    }
                }
                else
                {
                    hops.Add($"Hop {ttl}: * ({reply.Status})");
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // Fallback to tracepath on Linux if unprivileged Ping TTL is unsupported
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                try
                {
                    using var proc = Process.Start(new ProcessStartInfo
                    {
                        FileName = "tracepath",
                        Arguments = $"-n -m 5 {target}",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    });

                    if (proc is not null)
                    {
                        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                        cts.CancelAfter(3500);
                        string output = await proc.StandardOutput.ReadToEndAsync(cts.Token);
                        foreach (string line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Take(6))
                        {
                            hops.Add(line.Trim());
                        }
                    }
                }
                catch
                {
                    // Ignore fallback failure
                }
            }
        }

        sw.Stop();
        bool anyHops = hops.Any(h => !h.Contains("*"));
        return new DiagnosticCheckItem
        {
            Id = "route_trace",
            Title = "Route Tracing (Optional)",
            Category = "WAN & Internet",
            Status = anyHops ? HealthStatus.Healthy : HealthStatus.Inconclusive,
            WhatWasTested = $"Executed a bounded 5-hop route trace toward {target}.",
            MeasuredResult = anyHops ? $"Captured {hops.Count} hop(s)" : "Route hops did not respond to TTL expiry probes",
            WhyItMatters = "Shows intermediate network routers along the path to external destinations.",
            TechnicalDetails = hops.Count > 0 ? string.Join(" -> ", hops) : "No ICMP Time Exceeded messages received.",
            DurationMs = sw.Elapsed.TotalMilliseconds
        };
    }
}
