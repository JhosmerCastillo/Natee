using Natee.Core.Models;
using Natee.Infrastructure.SystemMetrics;
using Xunit;

namespace Natee.Tests.Unit;

[Trait("Category", "Unit")]
public sealed class SystemMetricsAndSensorsTests
{
    [Fact]
    public void CalculateMemoryMetrics_ComputesPhysicalRamUsageAccurately()
    {
        long total = 16L * 1024 * 1024 * 1024; // 16 GB
        long avail = 4L * 1024 * 1024 * 1024;  // 4 GB available -> 12 GB used (75.0%)

        var (t, u, a, pct) = PlatformMemoryMetricsProvider.CalculateMemoryMetrics(total, avail);

        Assert.Equal(total, t);
        Assert.Equal(12L * 1024 * 1024 * 1024, u);
        Assert.Equal(avail, a);
        Assert.Equal(75.0, pct);

        // Zero capacity edge case
        var zeroResult = PlatformMemoryMetricsProvider.CalculateMemoryMetrics(0, 100);
        Assert.Equal(0.0, zeroResult.UsagePercent);
    }

    [Fact]
    public void CreateVolumeMetric_DistinguishesZeroRemainingCapacityFromUnavailableVolume()
    {
        // Volume with 0 available bytes out of 500 GB -> Accessible, 100% used, "Zero remaining capacity"
        long cap = 500L * 1024 * 1024 * 1024;
        var fullVol = PlatformDiskMetricsProvider.CreateVolumeMetric("Root", "/", "ext4", cap, 0);

        Assert.True(fullVol.IsAccessible);
        Assert.Equal(100.0, fullVol.UsagePercent);
        Assert.Equal(0, fullVol.AvailableBytes);
        Assert.Contains("Zero remaining capacity", fullVol.StatusNote);

        // Unavailable volume reporting 0 total bytes -> IsAccessible = false
        var unavailVol = PlatformDiskMetricsProvider.CreateVolumeMetric("Disconnected", "/mnt/usb", "vfat", 0, 0);
        Assert.False(unavailVol.IsAccessible);
    }

    [Fact]
    public async Task HardwareSensorProvider_HandlesAvailableAndUnsupportedSensorsCorrectly()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "natee-sensor-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            // 1. Non-existent directory -> Unsupported (never fabricated)
            var unsupportedProvider = new PlatformHardwareSensorProvider(
                Path.Combine(tempRoot, "missing_hwmon"),
                Path.Combine(tempRoot, "missing_thermal"));

            var (temp1, status1, _) = await unsupportedProvider.GetCpuTemperatureAsync();
            Assert.Null(temp1);
            Assert.Equal(SensorAvailabilityStatus.Unsupported, status1);

            // 2. Simulated sysfs coretemp sensor -> Available (64.5 °C)
            string hwmon0 = Path.Combine(tempRoot, "hwmon", "hwmon0");
            Directory.CreateDirectory(hwmon0);
            await File.WriteAllTextAsync(Path.Combine(hwmon0, "name"), "coretemp\n");
            await File.WriteAllTextAsync(Path.Combine(hwmon0, "temp1_label"), "Package id 0\n");
            await File.WriteAllTextAsync(Path.Combine(hwmon0, "temp1_input"), "64500\n");

            var availableProvider = new PlatformHardwareSensorProvider(
                Path.Combine(tempRoot, "hwmon"),
                Path.Combine(tempRoot, "missing_thermal"));

            var (temp2, status2, note2) = await availableProvider.GetCpuTemperatureAsync();
            Assert.NotNull(temp2);
            Assert.Equal(64.5, temp2!.Value);
            Assert.Equal(SensorAvailabilityStatus.Available, status2);
            Assert.Contains("coretemp", note2);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void FormatUptime_FormatsDaysHoursAndMinutesCorrectly()
    {
        Assert.Equal("2d 5h 30m", SystemMetricsSnapshot.FormatUptime(new TimeSpan(2, 5, 30, 10)));
        Assert.Equal("0h 45m 12s", SystemMetricsSnapshot.FormatUptime(new TimeSpan(0, 0, 45, 12)));
        Assert.Equal("0h 0m 0s", SystemMetricsSnapshot.FormatUptime(TimeSpan.FromSeconds(-5)));
    }
}
