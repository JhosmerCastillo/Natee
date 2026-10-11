using System.Runtime.InteropServices;
using Natee.Core.Interfaces;

namespace Natee.Infrastructure.SystemMetrics;

/// <summary>
/// Collects physical RAM capacity, used RAM, available RAM, and usage percentage.
/// Strictly measures physical memory without mixing swap usage.
/// </summary>
public sealed class PlatformMemoryMetricsProvider : IMemoryMetricsProvider
{
    public Task<(long TotalBytes, long UsedBytes, long AvailableBytes, double UsagePercent)> GetMemoryMetricsAsync(
        CancellationToken cancellationToken = default)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return Task.FromResult(ReadLinuxProcMemInfo());
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return Task.FromResult(ReadWindowsGlobalMemoryStatus());
        }

        return Task.FromResult(FallbackGcMemoryInfo());
    }

    public static (long TotalBytes, long UsedBytes, long AvailableBytes, double UsagePercent) CalculateMemoryMetrics(
        long totalBytes,
        long availableBytes)
    {
        if (totalBytes <= 0)
        {
            return (0, 0, 0, 0.0);
        }

        long clampedAvail = Math.Clamp(availableBytes, 0, totalBytes);
        long usedBytes = totalBytes - clampedAvail;
        double usagePercent = Math.Round((usedBytes * 100.0) / totalBytes, 1);
        return (totalBytes, usedBytes, clampedAvail, usagePercent);
    }

    private static (long TotalBytes, long UsedBytes, long AvailableBytes, double UsagePercent) ReadLinuxProcMemInfo()
    {
        try
        {
            if (!File.Exists("/proc/meminfo"))
            {
                return FallbackGcMemoryInfo();
            }

            long memTotalKb = 0;
            long memAvailableKb = -1;
            long memFreeKb = 0;
            long buffersKb = 0;
            long cachedKb = 0;

            foreach (string line in File.ReadLines("/proc/meminfo"))
            {
                if (line.StartsWith("MemTotal:", StringComparison.Ordinal))
                {
                    memTotalKb = ParseMemInfoKb(line);
                }
                else if (line.StartsWith("MemAvailable:", StringComparison.Ordinal))
                {
                    memAvailableKb = ParseMemInfoKb(line);
                }
                else if (line.StartsWith("MemFree:", StringComparison.Ordinal))
                {
                    memFreeKb = ParseMemInfoKb(line);
                }
                else if (line.StartsWith("Buffers:", StringComparison.Ordinal))
                {
                    buffersKb = ParseMemInfoKb(line);
                }
                else if (line.StartsWith("Cached:", StringComparison.Ordinal))
                {
                    cachedKb = ParseMemInfoKb(line);
                }
            }

            if (memTotalKb <= 0)
            {
                return FallbackGcMemoryInfo();
            }

            long effectiveAvailKb = memAvailableKb >= 0 ? memAvailableKb : (memFreeKb + buffersKb + cachedKb);
            return CalculateMemoryMetrics(memTotalKb * 1024L, effectiveAvailKb * 1024L);
        }
        catch
        {
            return FallbackGcMemoryInfo();
        }
    }

    private static long ParseMemInfoKb(string line)
    {
        string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 && long.TryParse(parts[1], out long kb) ? kb : 0;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    private static (long TotalBytes, long UsedBytes, long AvailableBytes, double UsagePercent) ReadWindowsGlobalMemoryStatus()
    {
        try
        {
            var memStatus = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
            if (GlobalMemoryStatusEx(ref memStatus) && memStatus.ullTotalPhys > 0)
            {
                return CalculateMemoryMetrics((long)memStatus.ullTotalPhys, (long)memStatus.ullAvailPhys);
            }
        }
        catch
        {
            // Fall through to managed GC memory info
        }

        return FallbackGcMemoryInfo();
    }

    private static (long TotalBytes, long UsedBytes, long AvailableBytes, double UsagePercent) FallbackGcMemoryInfo()
    {
        var gcInfo = GC.GetGCMemoryInfo();
        long total = gcInfo.TotalAvailableMemoryBytes;
        long used = Math.Clamp(gcInfo.MemoryLoadBytes, 0, total);
        long avail = Math.Max(0, total - used);
        return CalculateMemoryMetrics(total, avail);
    }
}
