using System.Diagnostics;
using System.Runtime.InteropServices;
using Natee.Core.Interfaces;
using Natee.Core.Models;

namespace Natee.Infrastructure.SystemMetrics;

/// <summary>
/// Collects operating-system uptime and top resource-consuming processes with CPU% normalized across logical processors.
/// Handles process termination and permission restrictions safely without requiring administrator privileges.
/// </summary>
public sealed class PlatformProcessMonitorProvider : IProcessMonitorProvider
{
    private readonly Dictionary<int, (TimeSpan CpuTime, DateTimeOffset Timestamp)> _previousProcessCpu = new();
    private readonly object _lock = new();

    public TimeSpan GetSystemUptime()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) && File.Exists("/proc/uptime"))
        {
            try
            {
                string text = File.ReadAllText("/proc/uptime").Trim();
                string[] parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 0 &&
                    double.TryParse(
                        parts[0],
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out double uptimeSeconds) &&
                    uptimeSeconds > 0)
                {
                    return TimeSpan.FromSeconds(uptimeSeconds);
                }
            }
            catch
            {
                // Fall back to Environment.TickCount64
            }
        }

        return TimeSpan.FromMilliseconds(Math.Max(0, Environment.TickCount64));
    }

    public Task<IReadOnlyList<ProcessResourceMetric>> GetTopProcessesAsync(
        int maxEntries,
        ProcessSortMode sortMode,
        CancellationToken cancellationToken = default)
    {
        int clampedMax = Math.Clamp(maxEntries, 1, 25);
        int logicalCores = Math.Max(1, Environment.ProcessorCount);
        var now = DateTimeOffset.UtcNow;

        Process[] processes;
        try
        {
            processes = Process.GetProcesses();
        }
        catch
        {
            return Task.FromResult<IReadOnlyList<ProcessResourceMetric>>(Array.Empty<ProcessResourceMetric>());
        }

        var metrics = new List<ProcessResourceMetric>(processes.Length);
        var nextSamples = new Dictionary<int, (TimeSpan CpuTime, DateTimeOffset Timestamp)>();

        lock (_lock)
        {
            foreach (var proc in processes)
            {
                try
                {
                    int pid = proc.Id;
                    if (pid <= 0)
                    {
                        continue;
                    }

                    string name = proc.ProcessName;
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        continue;
                    }

                    long workingSet = 0;
                    try
                    {
                        workingSet = proc.WorkingSet64;
                    }
                    catch
                    {
                        // Restricted process memory query
                    }

                    double cpuPercent = 0.0;
                    try
                    {
                        TimeSpan totalCpu = proc.TotalProcessorTime;
                        nextSamples[pid] = (totalCpu, now);

                        if (_previousProcessCpu.TryGetValue(pid, out var prev))
                        {
                            double elapsedWallMs = (now - prev.Timestamp).TotalMilliseconds;
                            double deltaCpuMs = (totalCpu - prev.CpuTime).TotalMilliseconds;
                            if (elapsedWallMs > 25 && deltaCpuMs >= 0)
                            {
                                double normalized = (deltaCpuMs / (elapsedWallMs * logicalCores)) * 100.0;
                                cpuPercent = Math.Round(Math.Clamp(normalized, 0.0, 100.0), 1);
                            }
                        }
                    }
                    catch
                    {
                        // Process exited or restricted CPU time query
                    }

                    metrics.Add(new ProcessResourceMetric
                    {
                        ProcessId = pid,
                        ProcessName = name,
                        ExecutablePath = null, // Avoid expensive MainModule lookups across all processes
                        CpuUsagePercent = cpuPercent,
                        MemoryBytes = Math.Max(0, workingSet)
                    });
                }
                catch
                {
                    // Process terminated during enumeration
                }
                finally
                {
                    try
                    {
                        proc.Dispose();
                    }
                    catch
                    {
                        // Ignore dispose error
                    }
                }
            }

            _previousProcessCpu.Clear();
            foreach (var kvp in nextSamples)
            {
                _previousProcessCpu[kvp.Key] = kvp.Value;
            }
        }

        IEnumerable<ProcessResourceMetric> sorted = sortMode == ProcessSortMode.MemoryDescending
            ? metrics.OrderByDescending(m => m.MemoryBytes).ThenByDescending(m => m.CpuUsagePercent)
            : metrics.OrderByDescending(m => m.CpuUsagePercent).ThenByDescending(m => m.MemoryBytes);

        return Task.FromResult<IReadOnlyList<ProcessResourceMetric>>(sorted.Take(clampedMax).ToList());
    }
}
