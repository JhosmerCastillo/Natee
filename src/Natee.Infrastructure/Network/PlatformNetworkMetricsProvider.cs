using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Natee.Core.Interfaces;
using Natee.Core.Models;
using Natee.Core.Services;

namespace Natee.Infrastructure.Network;

/// <summary>
/// Cross-platform network interface, throughput counter, and connectivity probe provider for Windows and Linux.
/// Uses real system network counters and avoids excessive polling or fabricated values.
/// </summary>
public sealed class PlatformNetworkMetricsProvider : INetworkMetricsProvider
{
    private string? _lastInterfaceId;
    private long _lastBytesReceived;
    private long _lastBytesSent;
    private DateTimeOffset _lastSampleTimestamp = DateTimeOffset.MinValue;
    private readonly object _counterLock = new();

    public async Task<NetworkSnapshot> CaptureSnapshotAsync(
        IReadOnlyList<string> probeTargets,
        int probeTimeoutMs,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        NetworkInterface? activeNic = SelectPrimaryInterface();

        if (activeNic is null)
        {
            return new NetworkSnapshot
            {
                TimestampUtc = now,
                LatestCheckTimestampUtc = now,
                ConnectionStatus = HealthStatus.Failed,
                ConnectionStatusSummary = "Offline — No active network interface detected.",
                ActiveInterfaceId = string.Empty,
                ActiveInterfaceName = "None",
                ConnectionType = "Disconnected"
            };
        }

        IPInterfaceProperties? ipProps = null;
        try
        {
            ipProps = activeNic.GetIPProperties();
        }
        catch
        {
            // Interface may have transitioned state during query
        }

        string? ipv4 = ipProps?.UnicastAddresses
            .FirstOrDefault(u => u.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(u.Address))
            ?.Address.ToString();

        string? ipv6 = ipProps?.UnicastAddresses
            .FirstOrDefault(u => u.Address.AddressFamily == AddressFamily.InterNetworkV6 && !IPAddress.IsLoopback(u.Address))
            ?.Address.ToString();

        string? gateway = ipProps?.GatewayAddresses
            .Select(g => g.Address)
            .FirstOrDefault(a => !a.Equals(IPAddress.Any) && !a.Equals(IPAddress.IPv6Any))
            ?.ToString();

        if (string.IsNullOrWhiteSpace(gateway) && RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            gateway = TryReadLinuxDefaultGateway(activeNic.Name);
        }

        var dnsServers = ipProps?.DnsAddresses
            .Select(d => d.ToString())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? new List<string>();

        if (dnsServers.Count == 0 && RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            dnsServers = TryReadLinuxResolvConf();
        }

        string? macAddress = FormatMacAddress(activeNic);
        string connectionType = FormatConnectionType(activeNic);
        string? wifiSsid = connectionType.Contains("Wi-Fi", StringComparison.OrdinalIgnoreCase)
            ? TryReadWifiSsid(activeNic.Name)
            : null;

        var (rxBytes, txBytes) = ReadInterfaceByteCounters(activeNic);
        double downMbps = 0.0;
        double upMbps = 0.0;

        lock (_counterLock)
        {
            if (_lastInterfaceId == activeNic.Id && _lastSampleTimestamp != DateTimeOffset.MinValue)
            {
                TimeSpan elapsed = now - _lastSampleTimestamp;
                downMbps = NetworkClassifier.CalculateThroughputMbps(_lastBytesReceived, rxBytes, elapsed);
                upMbps = NetworkClassifier.CalculateThroughputMbps(_lastBytesSent, txBytes, elapsed);
            }

            _lastInterfaceId = activeNic.Id;
            _lastBytesReceived = rxBytes;
            _lastBytesSent = txBytes;
            _lastSampleTimestamp = now;
        }

        bool hasLocalIp = !string.IsNullOrWhiteSpace(ipv4) || !string.IsNullOrWhiteSpace(ipv6);
        var targetsToProbe = (probeTargets is { Count: > 0 } ? probeTargets : new[] { "1.1.1.1", "8.8.8.8", "9.9.9.9" })
            .Take(5)
            .ToList();

        IReadOnlyList<ProbeSample> samples = Array.Empty<ProbeSample>();
        if (hasLocalIp && activeNic.NetworkInterfaceType != NetworkInterfaceType.Loopback)
        {
            var probeTasks = targetsToProbe.Select(t => ProbeHostAsync(t, probeTimeoutMs, cancellationToken));
            samples = await Task.WhenAll(probeTasks);
        }

        var (avgLatency, lossPercent) = NetworkClassifier.CalculateProbeMetrics(samples);
        var (status, summary) = NetworkClassifier.EvaluateConnectivity(
            hasActiveInterface: activeNic.OperationalStatus == OperationalStatus.Up && activeNic.NetworkInterfaceType != NetworkInterfaceType.Loopback,
            hasLocalIp: hasLocalIp,
            samples: samples);

        int succeeded = samples.Count(s => s.IsSuccess);
        string probeSummary = samples.Count > 0
            ? $"{succeeded}/{samples.Count} targets reachable ({string.Join(", ", targetsToProbe)})"
            : "No external probes executed";

        return new NetworkSnapshot
        {
            TimestampUtc = now,
            LatestCheckTimestampUtc = DateTimeOffset.UtcNow,
            ConnectionStatus = status,
            ConnectionStatusSummary = summary,
            ActiveInterfaceId = activeNic.Id,
            ActiveInterfaceName = activeNic.Name,
            ConnectionType = connectionType,
            MacAddress = macAddress,
            WifiSsid = wifiSsid,
            Ipv4Address = ipv4,
            Ipv6Address = ipv6,
            DefaultGateway = gateway,
            DnsServers = dnsServers,
            DownloadThroughputMbps = downMbps,
            UploadThroughputMbps = upMbps,
            TotalBytesReceived = rxBytes,
            TotalBytesSent = txBytes,
            LatencyMs = avgLatency,
            PacketLossPercent = lossPercent,
            ProbeTargetSummary = probeSummary
        };
    }

    public static NetworkInterface? SelectPrimaryInterface()
    {
        try
        {
            var all = NetworkInterface.GetAllNetworkInterfaces();
            var operationalNonLoopback = all
                .Where(n => n.OperationalStatus == OperationalStatus.Up &&
                            n.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                            !n.Name.StartsWith("docker", StringComparison.OrdinalIgnoreCase) &&
                            !n.Name.StartsWith("veth", StringComparison.OrdinalIgnoreCase) &&
                            !n.Name.StartsWith("br-", StringComparison.OrdinalIgnoreCase) &&
                            !n.Name.StartsWith("virbr", StringComparison.OrdinalIgnoreCase))
                .ToList();

            // Prefer an operational interface with a configured gateway and IPv4 address
            var withGateway = operationalNonLoopback.FirstOrDefault(n =>
            {
                try
                {
                    var props = n.GetIPProperties();
                    return props.GatewayAddresses.Any(g => !g.Address.Equals(IPAddress.Any)) &&
                           props.UnicastAddresses.Any(u => u.Address.AddressFamily == AddressFamily.InterNetwork);
                }
                catch
                {
                    return false;
                }
            });

            if (withGateway is not null)
            {
                return withGateway;
            }

            var withIpv4 = operationalNonLoopback.FirstOrDefault(n =>
            {
                try
                {
                    return n.GetIPProperties().UnicastAddresses
                        .Any(u => u.Address.AddressFamily == AddressFamily.InterNetwork);
                }
                catch
                {
                    return false;
                }
            });

            if (withIpv4 is not null)
            {
                return withIpv4;
            }

            return operationalNonLoopback.FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    public static async Task<ProbeSample> ProbeHostAsync(
        string hostOrIp,
        int timeoutMs,
        CancellationToken cancellationToken = default)
    {
        int clampedTimeout = Math.Clamp(timeoutMs, 300, 10000);

        // 1. Try managed ICMP Ping first
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(hostOrIp, TimeSpan.FromMilliseconds(clampedTimeout), buffer: null, options: null, cancellationToken);
            if (reply.Status == IPStatus.Success)
            {
                return new ProbeSample(hostOrIp, true, reply.RoundtripTime);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // Fall through to TCP connect probe if unprivileged ICMP socket is blocked
        }

        // 2. Fallback: timed TCP handshake probe on HTTPS (443) or DNS (53)
        int[] candidatePorts = { 443, 53 };
        foreach (int port in candidatePorts)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(clampedTimeout);
                using var tcp = new TcpClient();
                await tcp.ConnectAsync(hostOrIp, port, cts.Token);
                sw.Stop();
                return new ProbeSample(hostOrIp, true, Math.Round(sw.Elapsed.TotalMilliseconds, 1));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // Try next port
            }
        }

        return new ProbeSample(hostOrIp, false, null, "Host unreachable or timed out");
    }

    private static (long Received, long Sent) ReadInterfaceByteCounters(NetworkInterface nic)
    {
        try
        {
            var stats = nic.GetIPStatistics();
            if (stats.BytesReceived > 0 || stats.BytesSent > 0)
            {
                return (stats.BytesReceived, stats.BytesSent);
            }
        }
        catch
        {
            // Fallback to /proc/net/dev on Linux
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) && File.Exists("/proc/net/dev"))
        {
            try
            {
                foreach (string line in File.ReadLines("/proc/net/dev").Skip(2))
                {
                    int colon = line.IndexOf(':');
                    if (colon <= 0) continue;
                    string iface = line[..colon].Trim();
                    if (!string.Equals(iface, nic.Name, StringComparison.Ordinal)) continue;

                    string[] parts = line[(colon + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 9 &&
                        long.TryParse(parts[0], out long rx) &&
                        long.TryParse(parts[8], out long tx))
                    {
                        return (rx, tx);
                    }
                }
            }
            catch
            {
                // Ignore procfs read errors
            }
        }

        return (0, 0);
    }

    private static string? FormatMacAddress(NetworkInterface nic)
    {
        try
        {
            byte[] bytes = nic.GetPhysicalAddress().GetAddressBytes();
            if (bytes.Length == 0 || bytes.All(b => b == 0))
            {
                return null;
            }

            return string.Join(":", bytes.Select(b => b.ToString("X2")));
        }
        catch
        {
            return null;
        }
    }

    private static string FormatConnectionType(NetworkInterface nic)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            string wirelessDir = $"/sys/class/net/{nic.Name}/wireless";
            if (Directory.Exists(wirelessDir) || nic.Name.StartsWith("wl", StringComparison.OrdinalIgnoreCase))
            {
                return "Wi-Fi (802.11)";
            }

            if (nic.Name.StartsWith("en", StringComparison.OrdinalIgnoreCase) ||
                nic.Name.StartsWith("eth", StringComparison.OrdinalIgnoreCase))
            {
                return "Ethernet";
            }

            if (nic.Name.StartsWith("tun", StringComparison.OrdinalIgnoreCase) ||
                nic.Name.StartsWith("wg", StringComparison.OrdinalIgnoreCase) ||
                nic.Name.StartsWith("tap", StringComparison.OrdinalIgnoreCase))
            {
                return "VPN / Tunnel";
            }
        }

        return nic.NetworkInterfaceType switch
        {
            NetworkInterfaceType.Wireless80211 => "Wi-Fi (802.11)",
            NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet or NetworkInterfaceType.FastEthernetT => "Ethernet",
            NetworkInterfaceType.Ppp or NetworkInterfaceType.Tunnel => "VPN / Tunnel",
            NetworkInterfaceType.Loopback => "Loopback",
            _ => nic.NetworkInterfaceType.ToString()
        };
    }

    private static string? TryReadLinuxDefaultGateway(string interfaceName)
    {
        try
        {
            if (!File.Exists("/proc/net/route"))
            {
                return null;
            }

            foreach (string line in File.ReadLines("/proc/net/route").Skip(1))
            {
                string[] parts = line.Split(new[] { '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 3 &&
                    string.Equals(parts[0], interfaceName, StringComparison.Ordinal) &&
                    parts[1] == "00000000" &&
                    uint.TryParse(parts[2], System.Globalization.NumberStyles.HexNumber, null, out uint gwHex) &&
                    gwHex != 0)
                {
                    byte[] bytes = BitConverter.GetBytes(gwHex);
                    return new IPAddress(bytes).ToString();
                }
            }
        }
        catch
        {
            // Ignore procfs parse failure
        }

        return null;
    }

    private static List<string> TryReadLinuxResolvConf()
    {
        var servers = new List<string>();
        try
        {
            if (!File.Exists("/etc/resolv.conf"))
            {
                return servers;
            }

            foreach (string line in File.ReadLines("/etc/resolv.conf"))
            {
                string trimmed = line.Trim();
                if (trimmed.StartsWith("nameserver ", StringComparison.OrdinalIgnoreCase))
                {
                    string[] parts = trimmed.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2)
                    {
                        servers.Add(parts[1]);
                    }
                }
            }
        }
        catch
        {
            // Ignore resolv.conf read failure
        }

        return servers;
    }

    private static string? TryReadWifiSsid(string interfaceName)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                using var proc = Process.Start(new ProcessStartInfo
                {
                    FileName = "nmcli",
                    Arguments = $"-g GENERAL.CONNECTION device show {interfaceName}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });

                if (proc is not null && proc.WaitForExit(800))
                {
                    string output = proc.StandardOutput.ReadToEnd().Trim();
                    if (!string.IsNullOrWhiteSpace(output) && output != "--")
                    {
                        return output;
                    }
                }
            }
        }
        catch
        {
            // Ignore optional SSID lookup failure
        }

        return null;
    }
}
