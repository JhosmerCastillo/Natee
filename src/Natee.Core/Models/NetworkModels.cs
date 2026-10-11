namespace Natee.Core.Models;

/// <summary>
/// Point-in-time snapshot of the active network interface, configuration, throughput, and connectivity probe metrics.
/// </summary>
public sealed record NetworkSnapshot
{
    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;
    public HealthStatus ConnectionStatus { get; init; } = HealthStatus.Inconclusive;
    public string ConnectionStatusSummary { get; init; } = "Checking connectivity...";
    public string ActiveInterfaceId { get; init; } = string.Empty;
    public string ActiveInterfaceName { get; init; } = "None";
    public string ConnectionType { get; init; } = "Unknown";
    public string? MacAddress { get; init; }
    public string? WifiSsid { get; init; }
    public string? Ipv4Address { get; init; }
    public string? Ipv6Address { get; init; }
    public string? DefaultGateway { get; init; }
    public IReadOnlyList<string> DnsServers { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Currently observed receive traffic rate in Megabits per second (Mbps) over the sampling interval.
    /// Represents observed interface traffic, not maximum ISP bandwidth.
    /// </summary>
    public double DownloadThroughputMbps { get; init; }

    /// <summary>
    /// Currently observed transmit traffic rate in Megabits per second (Mbps) over the sampling interval.
    /// Represents observed interface traffic, not maximum ISP bandwidth.
    /// </summary>
    public double UploadThroughputMbps { get; init; }

    public long TotalBytesReceived { get; init; }
    public long TotalBytesSent { get; init; }

    /// <summary>
    /// Average round-trip latency in milliseconds from the latest multi-target probe, or null if no probes succeeded.
    /// </summary>
    public double? LatencyMs { get; init; }

    /// <summary>
    /// Percentage of lost probe packets (0.0 to 100.0) across the latest probe sample window.
    /// </summary>
    public double PacketLossPercent { get; init; }

    public DateTimeOffset LatestCheckTimestampUtc { get; init; } = DateTimeOffset.UtcNow;
    public string ProbeTargetSummary { get; init; } = string.Empty;
}

/// <summary>
/// Result of a single ping/TCP connectivity probe to a target host.
/// </summary>
public sealed record ProbeSample(
    string Target,
    bool IsSuccess,
    double? RoundTripTimeMs,
    string? ErrorReason = null);

/// <summary>
/// Result of an explicit, user-triggered Internet download and upload speed test.
/// </summary>
public sealed record SpeedTestResult
{
    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;
    public string ProviderName { get; init; } = "Cloudflare Edge HTTPS";
    public string DownloadEndpoint { get; init; } = string.Empty;
    public string UploadEndpoint { get; init; } = string.Empty;
    public double DownloadSpeedMbps { get; init; }
    public double UploadSpeedMbps { get; init; }
    public double? LatencyMs { get; init; }
    public long BytesDownloaded { get; init; }
    public long BytesUploaded { get; init; }
    public TimeSpan Duration { get; init; }
    public bool IsSuccess { get; init; }
    public string? ErrorMessage { get; init; }
    public string LimitationsNotice { get; init; } =
        "Measured via single-stream HTTPS transfer against the configured edge endpoint. Results reflect real transfer throughput to this test server and may be lower than multi-stream ISP peak capacity.";
}

/// <summary>
/// Progress update emitted during an active user-initiated speed test.
/// </summary>
public sealed record SpeedTestProgress(
    string Phase,
    double ProgressPercent,
    double CurrentMbps);
