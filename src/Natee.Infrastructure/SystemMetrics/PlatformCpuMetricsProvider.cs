using System.Runtime.InteropServices;
using Natee.Core.Interfaces;

namespace Natee.Infrastructure.SystemMetrics;

/// <summary>
/// Collects real overall CPU utilization percentage and current operating frequency in MHz
/// from OS counters (/proc/stat and /sys/devices/system/cpu/cpufreq on Linux; Kernel32 GetSystemTimes on Windows).
/// </summary>
public sealed class PlatformCpuMetricsProvider : ICpuMetricsProvider
{
    private ulong _prevIdleTicks;
    private ulong _prevTotalTicks;
    private string? _cachedModelName;
    private readonly object _lock = new();

    public async Task<(string ModelName, int LogicalCores, double UsagePercent, double? CurrentFrequencyMHz)> GetCpuMetricsAsync(
        CancellationToken cancellationToken = default)
    {
        int cores = Environment.ProcessorCount;
        string model = _cachedModelName ??= ReadCpuModelName();
        double usage = await MeasureCpuUsagePercentAsync(cancellationToken);
        double? freqMHz = ReadCurrentCpuFrequencyMHz();

        return (model, cores, usage, freqMHz);
    }

    private async Task<double> MeasureCpuUsagePercentAsync(CancellationToken cancellationToken)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            var first = ReadLinuxProcStatTicks();
            if (first.Total == 0)
            {
                return 0.0;
            }

            ulong prevIdle, prevTotal;
            lock (_lock)
            {
                prevIdle = _prevIdleTicks;
                prevTotal = _prevTotalTicks;
                _prevIdleTicks = first.Idle;
                _prevTotalTicks = first.Total;
            }

            if (prevTotal == 0 || first.Total <= prevTotal)
            {
                await Task.Delay(80, cancellationToken);
                var second = ReadLinuxProcStatTicks();
                lock (_lock)
                {
                    _prevIdleTicks = second.Idle;
                    _prevTotalTicks = second.Total;
                }

                return ComputeUsageFromDeltas(first.Idle, first.Total, second.Idle, second.Total);
            }

            return ComputeUsageFromDeltas(prevIdle, prevTotal, first.Idle, first.Total);
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var first = ReadWindowsSystemTimes();
            if (first.Total == 0)
            {
                return 0.0;
            }

            ulong prevIdle, prevTotal;
            lock (_lock)
            {
                prevIdle = _prevIdleTicks;
                prevTotal = _prevTotalTicks;
                _prevIdleTicks = first.Idle;
                _prevTotalTicks = first.Total;
            }

            if (prevTotal == 0 || first.Total <= prevTotal)
            {
                await Task.Delay(80, cancellationToken);
                var second = ReadWindowsSystemTimes();
                lock (_lock)
                {
                    _prevIdleTicks = second.Idle;
                    _prevTotalTicks = second.Total;
                }

                return ComputeUsageFromDeltas(first.Idle, first.Total, second.Idle, second.Total);
            }

            return ComputeUsageFromDeltas(prevIdle, prevTotal, first.Idle, first.Total);
        }

        return 0.0;
    }

    private static double ComputeUsageFromDeltas(ulong idle1, ulong total1, ulong idle2, ulong total2)
    {
        if (total2 <= total1)
        {
            return 0.0;
        }

        ulong totalDelta = total2 - total1;
        ulong idleDelta = idle2 >= idle1 ? idle2 - idle1 : 0;
        if (idleDelta >= totalDelta)
        {
            return 0.0;
        }

        double busyRatio = (double)(totalDelta - idleDelta) / totalDelta;
        return Math.Round(Math.Clamp(busyRatio * 100.0, 0.0, 100.0), 1);
    }

    private static (ulong Idle, ulong Total) ReadLinuxProcStatTicks()
    {
        try
        {
            if (!File.Exists("/proc/stat"))
            {
                return (0, 0);
            }

            string? firstLine = File.ReadLines("/proc/stat").FirstOrDefault();
            if (string.IsNullOrWhiteSpace(firstLine) || !firstLine.StartsWith("cpu ", StringComparison.Ordinal))
            {
                return (0, 0);
            }

            string[] parts = firstLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 5)
            {
                return (0, 0);
            }

            ulong total = 0;
            ulong idle = 0;
            for (int i = 1; i < parts.Length; i++)
            {
                if (ulong.TryParse(parts[i], out ulong val))
                {
                    total += val;
                    // Column 4 is idle, Column 5 is iowait
                    if (i is 4 or 5)
                    {
                        idle += val;
                    }
                }
            }

            return (idle, total);
        }
        catch
        {
            return (0, 0);
        }
    }

    private static double? ReadCurrentCpuFrequencyMHz()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            try
            {
                // 1. Prefer /sys/devices/system/cpu/cpu*/cpufreq/scaling_cur_freq (reported in kHz)
                string cpuBaseDir = "/sys/devices/system/cpu";
                if (Directory.Exists(cpuBaseDir))
                {
                    var freqsMHz = new List<double>();
                    foreach (string dir in Directory.EnumerateDirectories(cpuBaseDir, "cpu*"))
                    {
                        string freqPath = Path.Combine(dir, "cpufreq", "scaling_cur_freq");
                        if (File.Exists(freqPath) &&
                            long.TryParse(File.ReadAllText(freqPath).Trim(), out long khz) &&
                            khz > 0)
                        {
                            freqsMHz.Add(khz / 1000.0);
                        }
                    }

                    if (freqsMHz.Count > 0)
                    {
                        return Math.Round(freqsMHz.Average(), 0);
                    }
                }

                // 2. Fallback to /proc/cpuinfo "cpu MHz" (also reflects current operating frequency on Linux x86)
                if (File.Exists("/proc/cpuinfo"))
                {
                    var mhzValues = new List<double>();
                    foreach (string line in File.ReadLines("/proc/cpuinfo"))
                    {
                        if (line.StartsWith("cpu MHz", StringComparison.OrdinalIgnoreCase))
                        {
                            int colon = line.IndexOf(':');
                            if (colon > 0 &&
                                double.TryParse(
                                    line[(colon + 1)..].Trim(),
                                    System.Globalization.NumberStyles.Float,
                                    System.Globalization.CultureInfo.InvariantCulture,
                                    out double mhz) &&
                                mhz > 0)
                            {
                                mhzValues.Add(mhz);
                            }
                        }
                    }

                    if (mhzValues.Count > 0)
                    {
                        return Math.Round(mhzValues.Average(), 0);
                    }
                }
            }
            catch
            {
                // Ignore frequency read error
            }
        }

        // Note: Windows Registry ~MHz reports nominal base clock rather than real-time dynamic frequency;
        // per specification ("Do not confuse a processor's nominal maximum frequency with its currently measured operating frequency"),
        // we return null when real-time operating frequency is not exposed without privileged counters.
        return null;
    }

    private static string ReadCpuModelName()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            try
            {
                if (File.Exists("/proc/cpuinfo"))
                {
                    foreach (string line in File.ReadLines("/proc/cpuinfo"))
                    {
                        if (line.StartsWith("model name", StringComparison.OrdinalIgnoreCase) ||
                            line.StartsWith("Hardware", StringComparison.OrdinalIgnoreCase))
                        {
                            int colon = line.IndexOf(':');
                            if (colon > 0)
                            {
                                string val = line[(colon + 1)..].Trim();
                                if (!string.IsNullOrWhiteSpace(val))
                                {
                                    return val;
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
                // Ignore
            }
        }

        string? envProc = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER");
        if (!string.IsNullOrWhiteSpace(envProc))
        {
            return envProc;
        }

        return $"{RuntimeInformation.ProcessArchitecture} Processor ({Environment.ProcessorCount} cores)";
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FILETIME
    {
        public uint dwLowDateTime;
        public uint dwHighDateTime;
        public readonly ulong ToUInt64() => ((ulong)dwHighDateTime << 32) | dwLowDateTime;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out FILETIME lpIdleTime, out FILETIME lpKernelTime, out FILETIME lpUserTime);

    private static (ulong Idle, ulong Total) ReadWindowsSystemTimes()
    {
        try
        {
            if (GetSystemTimes(out var idle, out var kernel, out var user))
            {
                ulong idleTicks = idle.ToUInt64();
                // Kernel time on Windows includes Idle time
                ulong totalTicks = kernel.ToUInt64() + user.ToUInt64();
                return (idleTicks, totalTicks);
            }
        }
        catch
        {
            // Ignore P/Invoke error
        }

        return (0, 0);
    }
}
