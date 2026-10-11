namespace Natee.Core.Models;

/// <summary>
/// Represents capacity, available space, and optional throughput metrics for a mounted storage volume or logical disk.
/// </summary>
public sealed record StorageVolumeMetric
{
    public string VolumeId { get; init; } = string.Empty;
    public string MountPoint { get; init; } = string.Empty;
    public string FileSystemType { get; init; } = string.Empty;
    public long TotalBytes { get; init; }
    public long UsedBytes { get; init; }
    public long AvailableBytes { get; init; }
    public double UsagePercent { get; init; }
    public double? ReadThroughputMbps { get; init; }
    public double? WriteThroughputMbps { get; init; }
    public bool IsAccessible { get; init; } = true;
    public string? StatusNote { get; init; }

    public double TotalGigabytes => Math.Round(TotalBytes / (1024.0 * 1024.0 * 1024.0), 2);
    public double UsedGigabytes => Math.Round(UsedBytes / (1024.0 * 1024.0 * 1024.0), 2);
    public double AvailableGigabytes => Math.Round(AvailableBytes / (1024.0 * 1024.0 * 1024.0), 2);
}

/// <summary>
/// Represents CPU and physical memory resource usage for a single running process.
/// </summary>
public sealed record ProcessResourceMetric
{
    public int ProcessId { get; init; }
    public string ProcessName { get; init; } = string.Empty;
    public string? ExecutablePath { get; init; }

    /// <summary>
    /// Process CPU utilization normalized across all logical processors (0.0 to 100.0%).
    /// </summary>
    public double CpuUsagePercent { get; init; }

    public long MemoryBytes { get; init; }
    public double MemoryMegabytes => Math.Round(MemoryBytes / (1024.0 * 1024.0), 1);
}

/// <summary>
/// Unified snapshot of CPU, physical memory, operating-system uptime, storage volumes, and top processes.
/// </summary>
public sealed record SystemMetricsSnapshot
{
    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;
    public string Hostname { get; init; } = string.Empty;
    public string Username { get; init; } = string.Empty;
    public string OsDescription { get; init; } = string.Empty;
    public string CpuModel { get; init; } = "Unknown Processor";
    public int LogicalCores { get; init; } = Environment.ProcessorCount;

    public double CpuUsagePercent { get; init; }
    public double? CpuFrequencyMHz { get; init; }
    public double? CpuTemperatureCelsius { get; init; }
    public SensorAvailabilityStatus TemperatureSensorStatus { get; init; } = SensorAvailabilityStatus.Unsupported;
    public string TemperatureSensorNote { get; init; } = "Sensor not inspected.";

    public long TotalRamBytes { get; init; }
    public long UsedRamBytes { get; init; }
    public long AvailableRamBytes { get; init; }
    public double RamUsagePercent { get; init; }

    public double TotalRamGigabytes => Math.Round(TotalRamBytes / (1024.0 * 1024.0 * 1024.0), 2);
    public double UsedRamGigabytes => Math.Round(UsedRamBytes / (1024.0 * 1024.0 * 1024.0), 2);
    public double AvailableRamGigabytes => Math.Round(AvailableRamBytes / (1024.0 * 1024.0 * 1024.0), 2);

    public TimeSpan SystemUptime { get; init; }
    public string FormattedUptime => FormatUptime(SystemUptime);

    public IReadOnlyList<StorageVolumeMetric> Volumes { get; init; } = Array.Empty<StorageVolumeMetric>();
    public IReadOnlyList<ProcessResourceMetric> TopProcesses { get; init; } = Array.Empty<ProcessResourceMetric>();

    public static string FormatUptime(TimeSpan uptime)
    {
        if (uptime < TimeSpan.Zero)
        {
            uptime = TimeSpan.Zero;
        }

        if (uptime.TotalDays >= 1)
        {
            return $"{(int)uptime.TotalDays}d {uptime.Hours}h {uptime.Minutes}m";
        }

        return $"{uptime.Hours}h {uptime.Minutes}m {uptime.Seconds}s";
    }
}
