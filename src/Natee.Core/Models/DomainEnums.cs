namespace Natee.Core.Models;

/// <summary>
/// Represents the normalized health or operational status of a metric or diagnostic check.
/// </summary>
public enum HealthStatus
{
    Healthy = 0,
    Degraded = 1,
    Failed = 2,
    Inconclusive = 3,
    Unsupported = 4
}

/// <summary>
/// Distinguishes between available hardware sensors, temporarily inaccessible sensors, and unsupported hardware.
/// </summary>
public enum SensorAvailabilityStatus
{
    Available = 0,
    TemporarilyInaccessible = 1,
    Unsupported = 2
}

/// <summary>
/// Identifies the category of an intelligent alert or recovery notification.
/// </summary>
public enum AlertType
{
    InternetConnectionLost = 0,
    InternetConnectionRestored = 1,
    HighLatency = 2,
    LatencyRecovered = 3,
    HighPacketLoss = 4,
    PacketLossRecovered = 5,
    LowDiskSpace = 6,
    DiskSpaceRecovered = 7,
    HighCpuPressure = 8,
    CpuPressureRecovered = 9,
    HighRamPressure = 10,
    RamPressureRecovered = 11
}

/// <summary>
/// User preference for application color theme.
/// </summary>
public enum ThemePreference
{
    System = 0,
    Light = 1,
    Dark = 2
}

/// <summary>
/// Process table sorting mode.
/// </summary>
public enum ProcessSortMode
{
    CpuDescending = 0,
    MemoryDescending = 1
}
