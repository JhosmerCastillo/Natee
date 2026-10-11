using System.Text.RegularExpressions;
using Natee.Core.Models;

namespace Natee.Core.Services;

/// <summary>
/// Applies user-selected privacy redactions to technical report documents prior to PDF and JSON serialization.
/// Ensures excluded values are removed from structured fields as well as free-text diagnostic details.
/// </summary>
public static partial class PrivacyRedactionService
{
    public const string RedactedHostnameToken = "[REDACTED-HOSTNAME]";
    public const string RedactedUsernameToken = "[REDACTED-USER]";
    public const string RedactedLocalIpToken = "[REDACTED-LOCAL-IP]";
    public const string RedactedPublicIpToken = "[REDACTED-PUBLIC-IP]";
    public const string RedactedMacToken = "[REDACTED-MAC]";
    public const string RedactedInterfaceToken = "[REDACTED-INTERFACE]";
    public const string RedactedSsidToken = "[REDACTED-SSID]";
    public const string RedactedProcessToken = "[REDACTED-PROCESS]";
    public const string RedactedPathToken = "[REDACTED-PATH]";

    [GeneratedRegex(@"\b(?:(?:25[0-5]|2[0-4]\d|1?\d?\d)\.){3}(?:25[0-5]|2[0-4]\d|1?\d?\d)\b", RegexOptions.Compiled)]
    private static partial Regex Ipv4Regex();

    [GeneratedRegex(@"\b(?:[0-9a-fA-F]{1,4}:){2,7}[0-9a-fA-F]{1,4}(?:%[0-9a-zA-Z]+)?\b", RegexOptions.Compiled)]
    private static partial Regex Ipv6Regex();

    [GeneratedRegex(@"\b(?:[0-9A-Fa-f]{2}[:-]){5}[0-9A-Fa-f]{2}\b", RegexOptions.Compiled)]
    private static partial Regex MacAddressRegex();

    public static TechnicalReportDocument ApplyRedaction(
        TechnicalReportDocument source,
        PrivacyRedactionOptions options)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(options);

        var excludedSummary = BuildExcludedFieldsSummary(options);
        var literalTokensToScrub = new List<(string RawValue, string Replacement)>();

        void AddLiteralScrub(string? raw, string replacement)
        {
            if (!string.IsNullOrWhiteSpace(raw) &&
                raw.Length >= 2 &&
                !string.Equals(raw, "None", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(raw, "Unknown", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(raw, "Unavailable", StringComparison.OrdinalIgnoreCase))
            {
                literalTokensToScrub.Add((raw, replacement));
            }
        }

        if (options.RedactHostname)
        {
            AddLiteralScrub(source.System.Hostname, RedactedHostnameToken);
        }

        if (options.RedactUsername)
        {
            AddLiteralScrub(source.System.Username, RedactedUsernameToken);
        }

        if (options.RedactLocalIpAddresses)
        {
            AddLiteralScrub(source.Network.Ipv4Address, RedactedLocalIpToken);
            AddLiteralScrub(source.Network.Ipv6Address, RedactedLocalIpToken);
            AddLiteralScrub(source.Network.DefaultGateway, RedactedLocalIpToken);
            foreach (var dns in source.Network.DnsServers)
            {
                if (IsPrivateOrLocalIp(dns))
                {
                    AddLiteralScrub(dns, RedactedLocalIpToken);
                }
            }
        }

        if (options.RedactPublicIpAddresses)
        {
            foreach (var dns in source.Network.DnsServers)
            {
                if (!IsPrivateOrLocalIp(dns))
                {
                    AddLiteralScrub(dns, RedactedPublicIpToken);
                }
            }
        }

        if (options.RedactMacAddresses)
        {
            AddLiteralScrub(source.Network.MacAddress, RedactedMacToken);
        }

        if (options.RedactInterfaceIdentifiers)
        {
            AddLiteralScrub(source.Network.ActiveInterfaceId, RedactedInterfaceToken);
            AddLiteralScrub(source.Network.ActiveInterfaceName, RedactedInterfaceToken);
        }

        if (options.RedactWifiSsid)
        {
            AddLiteralScrub(source.Network.WifiSsid, RedactedSsidToken);
        }

        if (options.RedactLocalFilePaths)
        {
            foreach (var vol in source.System.Volumes)
            {
                if (vol.MountPoint.Length > 1 && vol.MountPoint != "/")
                {
                    AddLiteralScrub(vol.MountPoint, RedactedPathToken);
                }
            }

            foreach (var proc in source.System.TopProcesses)
            {
                AddLiteralScrub(proc.ExecutablePath, RedactedPathToken);
            }
        }

        if (options.RedactProcessNames)
        {
            foreach (var proc in source.System.TopProcesses)
            {
                AddLiteralScrub(proc.ProcessName, RedactedProcessToken);
            }
        }

        string ScrubText(string? input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return string.Empty;
            }

            string text = input;

            // Replace known literal values first (longest first to prevent partial token collision)
            foreach (var (raw, replacement) in literalTokensToScrub.OrderByDescending(x => x.RawValue.Length))
            {
                text = text.Replace(raw, replacement, StringComparison.OrdinalIgnoreCase);
            }

            if (options.RedactMacAddresses)
            {
                text = MacAddressRegex().Replace(text, RedactedMacToken);
            }

            if (options.RedactLocalIpAddresses || options.RedactPublicIpAddresses)
            {
                text = Ipv4Regex().Replace(text, match =>
                {
                    bool isLocal = IsPrivateOrLocalIp(match.Value);
                    if (isLocal && options.RedactLocalIpAddresses)
                    {
                        return RedactedLocalIpToken;
                    }

                    if (!isLocal && options.RedactPublicIpAddresses)
                    {
                        return RedactedPublicIpToken;
                    }

                    return match.Value;
                });

                text = Ipv6Regex().Replace(text, match =>
                {
                    bool isLocal = IsPrivateOrLocalIp(match.Value);
                    if (isLocal && options.RedactLocalIpAddresses)
                    {
                        return RedactedLocalIpToken;
                    }

                    if (!isLocal && options.RedactPublicIpAddresses)
                    {
                        return RedactedPublicIpToken;
                    }

                    return match.Value;
                });
            }

            return text;
        }

        var redactedVolumes = source.System.Volumes.Select((v, index) => v with
        {
            VolumeId = options.RedactLocalFilePaths ? $"Volume-{index + 1}" : ScrubText(v.VolumeId),
            MountPoint = options.RedactLocalFilePaths ? RedactedPathToken : ScrubText(v.MountPoint),
            StatusNote = string.IsNullOrEmpty(v.StatusNote) ? v.StatusNote : ScrubText(v.StatusNote)
        }).ToList();

        var redactedProcesses = options.RedactProcessNames
            ? source.System.TopProcesses.Select((p, index) => p with
            {
                ProcessId = 0,
                ProcessName = $"{RedactedProcessToken}-{index + 1}",
                ExecutablePath = options.RedactLocalFilePaths ? RedactedPathToken : null
            }).ToList()
            : source.System.TopProcesses.Select(p => p with
            {
                ExecutablePath = options.RedactLocalFilePaths
                    ? (p.ExecutablePath is null ? null : RedactedPathToken)
                    : p.ExecutablePath
            }).ToList();

        var redactedSystem = source.System with
        {
            Hostname = options.RedactHostname ? RedactedHostnameToken : source.System.Hostname,
            Username = options.RedactUsername ? RedactedUsernameToken : source.System.Username,
            Volumes = redactedVolumes,
            TopProcesses = redactedProcesses
        };

        var redactedDns = source.Network.DnsServers.Select(dns =>
        {
            bool isLocal = IsPrivateOrLocalIp(dns);
            if (isLocal && options.RedactLocalIpAddresses)
            {
                return RedactedLocalIpToken;
            }

            if (!isLocal && options.RedactPublicIpAddresses)
            {
                return RedactedPublicIpToken;
            }

            return dns;
        }).ToList();

        var redactedNetwork = source.Network with
        {
            ActiveInterfaceId = options.RedactInterfaceIdentifiers ? RedactedInterfaceToken : source.Network.ActiveInterfaceId,
            ActiveInterfaceName = options.RedactInterfaceIdentifiers ? RedactedInterfaceToken : source.Network.ActiveInterfaceName,
            MacAddress = options.RedactMacAddresses
                ? (source.Network.MacAddress is null ? null : RedactedMacToken)
                : source.Network.MacAddress,
            WifiSsid = options.RedactWifiSsid
                ? (source.Network.WifiSsid is null ? null : RedactedSsidToken)
                : source.Network.WifiSsid,
            Ipv4Address = options.RedactLocalIpAddresses
                ? (source.Network.Ipv4Address is null ? null : RedactedLocalIpToken)
                : source.Network.Ipv4Address,
            Ipv6Address = options.RedactLocalIpAddresses
                ? (source.Network.Ipv6Address is null ? null : RedactedLocalIpToken)
                : source.Network.Ipv6Address,
            DefaultGateway = options.RedactLocalIpAddresses
                ? (source.Network.DefaultGateway is null ? null : RedactedLocalIpToken)
                : source.Network.DefaultGateway,
            DnsServers = redactedDns,
            ConnectionStatusSummary = ScrubText(source.Network.ConnectionStatusSummary),
            ProbeTargetSummary = ScrubText(source.Network.ProbeTargetSummary)
        };

        DiagnosticReport? redactedDiagnosis = null;
        if (source.LatestDiagnosis is not null)
        {
            redactedDiagnosis = source.LatestDiagnosis with
            {
                SummaryTitle = ScrubText(source.LatestDiagnosis.SummaryTitle),
                SummaryExplanation = ScrubText(source.LatestDiagnosis.SummaryExplanation),
                LikelyCauses = source.LatestDiagnosis.LikelyCauses.Select(ScrubText).ToList(),
                UncertaintyNotice = source.LatestDiagnosis.UncertaintyNotice is null
                    ? null
                    : ScrubText(source.LatestDiagnosis.UncertaintyNotice),
                Checks = source.LatestDiagnosis.Checks.Select(c => c with
                {
                    WhatWasTested = ScrubText(c.WhatWasTested),
                    MeasuredResult = ScrubText(c.MeasuredResult),
                    WhyItMatters = ScrubText(c.WhyItMatters),
                    TechnicalDetails = ScrubText(c.TechnicalDetails)
                }).ToList()
            };
        }

        SpeedTestResult? redactedSpeedTest = null;
        if (source.LatestSpeedTest is not null)
        {
            redactedSpeedTest = source.LatestSpeedTest with
            {
                DownloadEndpoint = ScrubText(source.LatestSpeedTest.DownloadEndpoint),
                UploadEndpoint = ScrubText(source.LatestSpeedTest.UploadEndpoint),
                ErrorMessage = source.LatestSpeedTest.ErrorMessage is null
                    ? null
                    : ScrubText(source.LatestSpeedTest.ErrorMessage)
            };
        }

        var redactedAlerts = source.RecentAlerts.Select(a => a with
        {
            Title = ScrubText(a.Title),
            Message = ScrubText(a.Message)
        }).ToList();

        return source with
        {
            AppliedPrivacyOptions = options,
            ExcludedFieldsSummary = excludedSummary,
            System = redactedSystem,
            Network = redactedNetwork,
            LatestDiagnosis = redactedDiagnosis,
            LatestSpeedTest = redactedSpeedTest,
            RecentAlerts = redactedAlerts,
            ActiveWarnings = source.ActiveWarnings.Select(ScrubText).ToList(),
            UnavailableMeasurements = source.UnavailableMeasurements.Select(ScrubText).ToList()
        };
    }

    public static IReadOnlyList<string> BuildExcludedFieldsSummary(PrivacyRedactionOptions options)
    {
        var list = new List<string>();
        if (options.RedactHostname) list.Add("Computer hostname omitted (reduces asset correlation in corporate inventories).");
        if (options.RedactUsername) list.Add("OS username omitted (protects personal identity).");
        if (options.RedactLocalIpAddresses) list.Add("Local IPv4/IPv6 and default gateway omitted (hides internal subnet layout; may limit VLAN/DHCP troubleshooting).");
        if (options.RedactPublicIpAddresses) list.Add("Public IP and external resolver addresses omitted (protects geographic/ISP identity).");
        if (options.RedactMacAddresses) list.Add("Hardware MAC address omitted (prevents hardware tracking).");
        if (options.RedactInterfaceIdentifiers) list.Add("Network adapter identifiers omitted.");
        if (options.RedactWifiSsid) list.Add("Wi-Fi SSID omitted (protects wireless location privacy).");
        if (options.RedactProcessNames) list.Add("Process names omitted (protects running application privacy; resource totals remain visible).");
        if (options.RedactLocalFilePaths) list.Add("Local mount paths and executable paths omitted.");
        return list;
    }

    public static bool IsPrivateOrLocalIp(string? ipString)
    {
        if (string.IsNullOrWhiteSpace(ipString))
        {
            return false;
        }

        string cleaned = ipString.Trim();
        int zoneIdx = cleaned.IndexOf('%');
        if (zoneIdx > 0)
        {
            cleaned = cleaned[..zoneIdx];
        }

        if (!System.Net.IPAddress.TryParse(cleaned, out var ip))
        {
            return false;
        }

        if (System.Net.IPAddress.IsLoopback(ip))
        {
            return true;
        }

        if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            byte[] bytes = ip.GetAddressBytes();
            return bytes[0] == 10 ||
                   (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) ||
                   (bytes[0] == 192 && bytes[1] == 168) ||
                   (bytes[0] == 169 && bytes[1] == 254) ||
                   bytes[0] == 127;
        }

        if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            if (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal)
            {
                return true;
            }

            byte[] bytes = ip.GetAddressBytes();
            // Unique Local Addresses fc00::/7
            return (bytes[0] & 0xFE) == 0xFC;
        }

        return false;
    }
}
