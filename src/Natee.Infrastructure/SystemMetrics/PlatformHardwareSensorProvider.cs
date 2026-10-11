using System.Runtime.InteropServices;
using Natee.Core.Interfaces;
using Natee.Core.Models;

namespace Natee.Infrastructure.SystemMetrics;

/// <summary>
/// Reads CPU temperature from non-invasive OS sensor interfaces (/sys/class/hwmon and /sys/class/thermal on Linux)
/// without installing kernel drivers or requiring administrator privileges.
/// Clearly distinguishes Available, TemporarilyInaccessible, and Unsupported sensor states.
/// </summary>
public sealed class PlatformHardwareSensorProvider : IHardwareSensorProvider
{
    private static readonly string[] PreferredLinuxCpuSensorNames =
    {
        "coretemp",
        "k10temp",
        "zenpower",
        "cpu_thermal",
        "x86_pkg_temp",
        "acpitz",
        "dell_smm",
        "thinkpad"
    };

    private readonly string _hwmonBasePath;
    private readonly string _thermalBasePath;

    public PlatformHardwareSensorProvider(
        string? customHwmonBasePath = null,
        string? customThermalBasePath = null)
    {
        _hwmonBasePath = customHwmonBasePath ?? "/sys/class/hwmon";
        _thermalBasePath = customThermalBasePath ?? "/sys/class/thermal";
    }

    public Task<(double? TemperatureCelsius, SensorAvailabilityStatus Status, string Note)> GetCpuTemperatureAsync(
        CancellationToken cancellationToken = default)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux) &&
            _hwmonBasePath == "/sys/class/hwmon")
        {
            return Task.FromResult<(double?, SensorAvailabilityStatus, string)>((
                null,
                SensorAvailabilityStatus.Unsupported,
                "CPU temperature is not exposed to unprivileged user-space applications on Windows without kernel-mode drivers."));
        }

        bool sawSensorNodes = false;
        bool sawAccessError = false;

        // 1. Inspect /sys/class/hwmon/hwmon*
        if (Directory.Exists(_hwmonBasePath))
        {
            try
            {
                var candidates = new List<(int Priority, double TempCelsius, string SourceDescription)>();
                foreach (string hwmonDir in Directory.EnumerateDirectories(_hwmonBasePath, "hwmon*"))
                {
                    string nameFile = Path.Combine(hwmonDir, "name");
                    string chipName = "hwmon";
                    if (File.Exists(nameFile))
                    {
                        try
                        {
                            chipName = File.ReadAllText(nameFile).Trim();
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                        {
                            sawAccessError = true;
                        }
                    }

                    int priority = GetChipPriority(chipName);
                    foreach (string inputFile in Directory.EnumerateFiles(hwmonDir, "temp*_input"))
                    {
                        sawSensorNodes = true;
                        try
                        {
                            string raw = File.ReadAllText(inputFile).Trim();
                            if (double.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double milliC))
                            {
                                double celsius = milliC >= 1000 ? milliC / 1000.0 : milliC;
                                if (celsius is >= 1.0 and <= 125.0)
                                {
                                    string labelFile = inputFile.Replace("_input", "_label", StringComparison.Ordinal);
                                    string label = File.Exists(labelFile) ? SafeReadFirstLine(labelFile) : Path.GetFileNameWithoutExtension(inputFile);
                                    int labelBonus = label.Contains("Package", StringComparison.OrdinalIgnoreCase) ||
                                                     label.Contains("Tctl", StringComparison.OrdinalIgnoreCase) ||
                                                     label.Contains("Tdie", StringComparison.OrdinalIgnoreCase)
                                        ? 0
                                        : 1;
                                    candidates.Add((priority * 10 + labelBonus, Math.Round(celsius, 1), $"{chipName} ({label})"));
                                }
                            }
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                        {
                            sawAccessError = true;
                        }
                    }
                }

                if (candidates.Count > 0)
                {
                    var best = candidates.OrderBy(c => c.Priority).First();
                    return Task.FromResult<(double?, SensorAvailabilityStatus, string)>((
                        best.TempCelsius,
                        SensorAvailabilityStatus.Available,
                        $"Sensor: {best.SourceDescription}"));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                sawAccessError = true;
            }
        }

        // 2. Fallback: /sys/class/thermal/thermal_zone*
        if (Directory.Exists(_thermalBasePath))
        {
            try
            {
                var zoneCandidates = new List<(int Priority, double TempCelsius, string ZoneType)>();
                foreach (string zoneDir in Directory.EnumerateDirectories(_thermalBasePath, "thermal_zone*"))
                {
                    string tempFile = Path.Combine(zoneDir, "temp");
                    string typeFile = Path.Combine(zoneDir, "type");
                    if (!File.Exists(tempFile))
                    {
                        continue;
                    }

                    sawSensorNodes = true;
                    try
                    {
                        string zoneType = File.Exists(typeFile) ? SafeReadFirstLine(typeFile) : Path.GetFileName(zoneDir);
                        string raw = File.ReadAllText(tempFile).Trim();
                        if (double.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double milliC))
                        {
                            double celsius = milliC >= 1000 ? milliC / 1000.0 : milliC;
                            if (celsius is >= 1.0 and <= 125.0)
                            {
                                zoneCandidates.Add((GetChipPriority(zoneType), Math.Round(celsius, 1), zoneType));
                            }
                        }
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        sawAccessError = true;
                    }
                }

                if (zoneCandidates.Count > 0)
                {
                    var best = zoneCandidates.OrderBy(c => c.Priority).First();
                    return Task.FromResult<(double?, SensorAvailabilityStatus, string)>((
                        best.TempCelsius,
                        SensorAvailabilityStatus.Available,
                        $"Thermal zone: {best.ZoneType}"));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                sawAccessError = true;
            }
        }

        if (sawAccessError && sawSensorNodes)
        {
            return Task.FromResult<(double?, SensorAvailabilityStatus, string)>((
                null,
                SensorAvailabilityStatus.TemporarilyInaccessible,
                "Hardware temperature sensor exists but is currently inaccessible due to OS permissions or driver state."));
        }

        return Task.FromResult<(double?, SensorAvailabilityStatus, string)>((
            null,
            SensorAvailabilityStatus.Unsupported,
            "No compatible CPU temperature sensor exposed by the operating system or hardware."));
    }

    private static int GetChipPriority(string chipName)
    {
        for (int i = 0; i < PreferredLinuxCpuSensorNames.Length; i++)
        {
            if (chipName.Contains(PreferredLinuxCpuSensorNames[i], StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return 50;
    }

    private static string SafeReadFirstLine(string path)
    {
        try
        {
            return File.ReadLines(path).FirstOrDefault()?.Trim() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }
}
