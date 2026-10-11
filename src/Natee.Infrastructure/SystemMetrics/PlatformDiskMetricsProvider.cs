using System.Runtime.InteropServices;
using Natee.Core.Interfaces;
using Natee.Core.Models;
using Natee.Core.Services;

namespace Natee.Infrastructure.SystemMetrics;

/// <summary>
/// Inspects mounted storage volumes and logical disks, calculating capacity, remaining space,
/// and optional disk read/write throughput while safely handling inaccessible or disconnected drives.
/// </summary>
public sealed class PlatformDiskMetricsProvider : IDiskMetricsProvider
{
    private static readonly HashSet<string> IgnoredLinuxFsTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "proc", "sysfs", "devtmpfs", "devpts", "tmpfs", "securityfs", "cgroup", "cgroup2",
        "pstore", "efivarfs", "bpf", "autofs", "mqueue", "hugetlbfs", "debugfs", "tracefs",
        "configfs", "fusectl", "selinuxfs", "nsfs", "overlay", "squashfs", "rpc_pipefs",
        "ramfs", "portal", "fuse.portal", "fuse.gvfsd-fuse"
    };

    private long _prevReadBytes;
    private long _prevWriteBytes;
    private DateTimeOffset _prevDiskSampleTime = DateTimeOffset.MinValue;
    private readonly object _ioLock = new();

    public Task<IReadOnlyList<StorageVolumeMetric>> GetStorageVolumesAsync(
        CancellationToken cancellationToken = default)
    {
        var results = new List<StorageVolumeMetric>();
        var (readMbps, writeMbps) = SampleLinuxDiskThroughput();

        DriveInfo[] drives;
        try
        {
            drives = DriveInfo.GetDrives();
        }
        catch
        {
            return Task.FromResult<IReadOnlyList<StorageVolumeMetric>>(results);
        }

        bool attachedPrimaryThroughput = false;
        var seenLinuxMounts = new HashSet<string>(StringComparer.Ordinal);

        foreach (var drive in drives)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if ( drive.DriveType is DriveType.Ram or DriveType.Unknown or DriveType.NoRootDirectory)
                {
                    continue;
                }

                string mountPoint = drive.Name;
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    if (!ShouldIncludeLinuxMount(mountPoint))
                    {
                        continue;
                    }

                    if (!seenLinuxMounts.Add(mountPoint))
                    {
                        continue;
                    }
                }

                if (!drive.IsReady)
                {
                    results.Add(new StorageVolumeMetric
                    {
                        VolumeId = mountPoint,
                        MountPoint = mountPoint,
                        FileSystemType = "Offline / Not Ready",
                        TotalBytes = 0,
                        UsedBytes = 0,
                        AvailableBytes = 0,
                        UsagePercent = 0,
                        IsAccessible = false,
                        StatusNote = "Volume is not ready or media is disconnected."
                    });
                    continue;
                }

                string fsType = string.Empty;
                try
                {
                    fsType = drive.DriveFormat;
                }
                catch
                {
                    fsType = "Unknown";
                }

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) && IgnoredLinuxFsTypes.Contains(fsType))
                {
                    continue;
                }

                long total = drive.TotalSize;
                long available = drive.AvailableFreeSpace;
                if (total <= 0)
                {
                    continue;
                }

                var metric = CreateVolumeMetric(
                    volumeId: FormatVolumeIdentifier(drive, mountPoint),
                    mountPoint: mountPoint,
                    fileSystemType: fsType,
                    totalBytes: total,
                    availableBytes: available,
                    readThroughputMbps: !attachedPrimaryThroughput ? readMbps : null,
                    writeThroughputMbps: !attachedPrimaryThroughput ? writeMbps : null);

                attachedPrimaryThroughput = true;
                results.Add(metric);
            }
            catch (UnauthorizedAccessException)
            {
                results.Add(new StorageVolumeMetric
                {
                    VolumeId = drive.Name,
                    MountPoint = drive.Name,
                    FileSystemType = "Restricted",
                    IsAccessible = false,
                    StatusNote = "Permission denied when querying volume capacity."
                });
            }
            catch (IOException ex)
            {
                results.Add(new StorageVolumeMetric
                {
                    VolumeId = drive.Name,
                    MountPoint = drive.Name,
                    FileSystemType = "I/O Error",
                    IsAccessible = false,
                    StatusNote = $"Volume unavailable: {ex.Message}"
                });
            }
            catch
            {
                // Skip malformed virtual mount entries safely
            }
        }

        return Task.FromResult<IReadOnlyList<StorageVolumeMetric>>(results);
    }

    public static StorageVolumeMetric CreateVolumeMetric(
        string volumeId,
        string mountPoint,
        string fileSystemType,
        long totalBytes,
        long availableBytes,
        double? readThroughputMbps = null,
        double? writeThroughputMbps = null)
    {
        if (totalBytes <= 0)
        {
            return new StorageVolumeMetric
            {
                VolumeId = volumeId,
                MountPoint = mountPoint,
                FileSystemType = fileSystemType,
                IsAccessible = false,
                StatusNote = "Volume reported zero total capacity."
            };
        }

        long clampedAvail = Math.Clamp(availableBytes, 0, totalBytes);
        long used = totalBytes - clampedAvail;
        double usagePct = Math.Round((used * 100.0) / totalBytes, 1);

        return new StorageVolumeMetric
        {
            VolumeId = volumeId,
            MountPoint = mountPoint,
            FileSystemType = fileSystemType,
            TotalBytes = totalBytes,
            UsedBytes = used,
            AvailableBytes = clampedAvail,
            UsagePercent = usagePct,
            ReadThroughputMbps = readThroughputMbps,
            WriteThroughputMbps = writeThroughputMbps,
            IsAccessible = true,
            StatusNote = clampedAvail == 0 ? "Zero remaining capacity" : "Healthy"
        };
    }

    private static bool ShouldIncludeLinuxMount(string mountPoint)
    {
        if (mountPoint is "/" or "/home" or "/boot" or "/boot/efi" or "/data" or "/srv" or "/opt")
        {
            return true;
        }

        if (mountPoint.StartsWith("/mnt/", StringComparison.Ordinal) ||
            mountPoint.StartsWith("/media/", StringComparison.Ordinal) ||
            mountPoint.StartsWith("/run/media/", StringComparison.Ordinal))
        {
            return true;
        }

        return false;
    }

    private static string FormatVolumeIdentifier(DriveInfo drive, string mountPoint)
    {
        try
        {
            string label = drive.VolumeLabel;
            if (!string.IsNullOrWhiteSpace(label) && !string.Equals(label, mountPoint, StringComparison.OrdinalIgnoreCase))
            {
                return $"{label} ({mountPoint})";
            }
        }
        catch
        {
            // Ignore label read error
        }

        return mountPoint;
    }

    private (double? ReadMbps, double? WriteMbps) SampleLinuxDiskThroughput()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || !File.Exists("/proc/diskstats"))
        {
            return (null, null);
        }

        try
        {
            var now = DateTimeOffset.UtcNow;
            long totalSectorsRead = 0;
            long totalSectorsWritten = 0;

            foreach (string line in File.ReadLines("/proc/diskstats"))
            {
                string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 10)
                {
                    continue;
                }

                string devName = parts[2];
                // Count whole block devices (nvme0n1, sda, vda) rather than partitions to avoid double counting
                bool isWholeDisk = (devName.StartsWith("nvme", StringComparison.Ordinal) && !devName.Contains('p')) ||
                                   ((devName.StartsWith("sd", StringComparison.Ordinal) || devName.StartsWith("vd", StringComparison.Ordinal)) &&
                                    !char.IsDigit(devName[^1]));

                if (!isWholeDisk)
                {
                    continue;
                }

                if (long.TryParse(parts[5], out long sectorsRead) &&
                    long.TryParse(parts[9], out long sectorsWritten))
                {
                    totalSectorsRead += sectorsRead;
                    totalSectorsWritten += sectorsWritten;
                }
            }

            long readBytes = totalSectorsRead * 512L;
            long writeBytes = totalSectorsWritten * 512L;

            lock (_ioLock)
            {
                double? readMbps = null;
                double? writeMbps = null;

                if (_prevDiskSampleTime != DateTimeOffset.MinValue)
                {
                    TimeSpan elapsed = now - _prevDiskSampleTime;
                    readMbps = NetworkClassifier.CalculateThroughputMbps(_prevReadBytes, readBytes, elapsed);
                    writeMbps = NetworkClassifier.CalculateThroughputMbps(_prevWriteBytes, writeBytes, elapsed);
                }

                _prevReadBytes = readBytes;
                _prevWriteBytes = writeBytes;
                _prevDiskSampleTime = now;
                return (readMbps, writeMbps);
            }
        }
        catch
        {
            return (null, null);
        }
    }
}
