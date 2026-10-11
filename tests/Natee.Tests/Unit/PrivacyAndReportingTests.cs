using System.IO.Compression;
using System.Text;
using Natee.Core.Models;
using Natee.Core.Services;
using Natee.Infrastructure.Reporting;
using Xunit;

namespace Natee.Tests.Unit;

[Trait("Category", "Unit")]
public sealed class PrivacyAndReportingTests
{
    private static TechnicalReportDocument CreateSampleSensitiveReport()
    {
        return new TechnicalReportDocument
        {
            ReportId = Guid.NewGuid(),
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            System = new SystemMetricsSnapshot
            {
                Hostname = "CORP-FINANCE-LAPTOP42",
                Username = "jdoe_secret_admin",
                OsDescription = "Fedora Linux 44",
                CpuModel = "Intel Core i7",
                CpuUsagePercent = 24.5,
                TotalRamBytes = 16L * 1024 * 1024 * 1024,
                UsedRamBytes = 8L * 1024 * 1024 * 1024,
                AvailableRamBytes = 8L * 1024 * 1024 * 1024,
                RamUsagePercent = 50.0,
                Volumes = new[]
                {
                    new StorageVolumeMetric
                    {
                        VolumeId = "EncryptedVault (/home/jdoe_secret_admin/private_vault)",
                        MountPoint = "/home/jdoe_secret_admin/private_vault",
                        FileSystemType = "btrfs",
                        TotalBytes = 500_000_000_000,
                        UsedBytes = 200_000_000_000,
                        AvailableBytes = 300_000_000_000,
                        UsagePercent = 40.0,
                        IsAccessible = true
                    }
                },
                TopProcesses = new[]
                {
                    new ProcessResourceMetric
                    {
                        ProcessId = 4421,
                        ProcessName = "confidential_payroll_client",
                        ExecutablePath = "/home/jdoe_secret_admin/bin/confidential_payroll_client",
                        CpuUsagePercent = 12.4,
                        MemoryBytes = 250 * 1024 * 1024
                    }
                }
            },
            Network = new NetworkSnapshot
            {
                ConnectionStatus = HealthStatus.Healthy,
                ConnectionStatusSummary = "Connected on CORP-FINANCE-LAPTOP42 via 192.168.42.77",
                ActiveInterfaceId = "wlp0s20f3-secret-id",
                ActiveInterfaceName = "wlp0s20f3",
                ConnectionType = "Wi-Fi (802.11)",
                MacAddress = "A4:B1:C2:D3:E4:F5",
                WifiSsid = "ExecutiveFloor_5G_Private",
                Ipv4Address = "192.168.42.77",
                Ipv6Address = "fe80::a6b1:c2ff:fed3:e4f5",
                DefaultGateway = "192.168.42.1",
                DnsServers = new[] { "192.168.42.1", "198.51.100.88" },
                ProbeTargetSummary = "Probed 198.51.100.88 from 192.168.42.77"
            },
            LatestDiagnosis = new DiagnosticReport
            {
                OverallStatus = HealthStatus.Healthy,
                SummaryTitle = "Healthy on CORP-FINANCE-LAPTOP42",
                SummaryExplanation = "User jdoe_secret_admin verified gateway 192.168.42.1 and MAC A4:B1:C2:D3:E4:F5 on SSID ExecutiveFloor_5G_Private.",
                Checks = new[]
                {
                    new DiagnosticCheckItem
                    {
                        Id = "gateway",
                        Title = "Default Gateway",
                        Status = HealthStatus.Healthy,
                        WhatWasTested = "Probed 192.168.42.1",
                        MeasuredResult = "192.168.42.1 responded in 2 ms",
                        TechnicalDetails = "Adapter wlp0s20f3 (A4:B1:C2:D3:E4:F5) reached 192.168.42.1 and 198.51.100.88"
                    }
                }
            }
        };
    }

    [Fact]
    public async Task GeneratePdfAndJsonReports_AppliesPrivacyRedactionsCompletelyToExportedFiles()
    {
        var rawReport = CreateSampleSensitiveReport();
        var strictOptions = new PrivacyRedactionOptions
        {
            RedactHostname = true,
            RedactUsername = true,
            RedactLocalIpAddresses = true,
            RedactPublicIpAddresses = true,
            RedactMacAddresses = true,
            RedactInterfaceIdentifiers = true,
            RedactWifiSsid = true,
            RedactProcessNames = true,
            RedactLocalFilePaths = true
        };

        var redacted = PrivacyRedactionService.ApplyRedaction(rawReport, strictOptions);
        var generator = new ReportGeneratorService();

        string tempDir = Path.Combine(Path.GetTempPath(), "natee-report-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            string jsonPath = Path.Combine(tempDir, "report.json");
            string pdfPath = Path.Combine(tempDir, "report.pdf");

            string jsonContent = await generator.GenerateJsonReportAsync(redacted, jsonPath);
            await generator.GeneratePdfReportAsync(redacted, pdfPath);

            Assert.True(File.Exists(jsonPath));
            Assert.True(File.Exists(pdfPath));

            byte[] pdfBytes = await File.ReadAllBytesAsync(pdfPath);
            Assert.True(pdfBytes.Length > 500);
            string pdfAscii = Encoding.ASCII.GetString(pdfBytes);
            Assert.StartsWith("%PDF-", pdfAscii);

            string decompressedPdfContent = ExtractAllPdfStreamsAsText(pdfBytes);

            string[] forbiddenSensitiveValues =
            {
                "CORP-FINANCE-LAPTOP42",
                "jdoe_secret_admin",
                "192.168.42.77",
                "192.168.42.1",
                "fe80::a6b1:c2ff:fed3:e4f5",
                "198.51.100.88",
                "A4:B1:C2:D3:E4:F5",
                "wlp0s20f3",
                "ExecutiveFloor_5G_Private",
                "confidential_payroll_client",
                "private_vault"
            };

            foreach (string secret in forbiddenSensitiveValues)
            {
                Assert.DoesNotContain(secret, jsonContent, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(secret, pdfAscii, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(secret, decompressedPdfContent, StringComparison.OrdinalIgnoreCase);
            }

            Assert.Contains(PrivacyRedactionService.RedactedHostnameToken, jsonContent);
            Assert.Contains(PrivacyRedactionService.RedactedUsernameToken, jsonContent);
            Assert.Contains(PrivacyRedactionService.RedactedLocalIpToken, jsonContent);
            Assert.Contains(PrivacyRedactionService.RedactedMacToken, jsonContent);
            Assert.Contains(PrivacyRedactionService.RedactedSsidToken, jsonContent);
            Assert.Contains(PrivacyRedactionService.RedactedProcessToken, jsonContent);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    private static string ExtractAllPdfStreamsAsText(byte[] pdfBytes)
    {
        var sb = new StringBuilder();
        string latin1 = Encoding.Latin1.GetString(pdfBytes);
        int idx = 0;

        while ((idx = latin1.IndexOf("stream", idx, StringComparison.Ordinal)) >= 0)
        {
            int start = idx + 6;
            if (start < latin1.Length && latin1[start] == '\r') start++;
            if (start < latin1.Length && latin1[start] == '\n') start++;

            int end = latin1.IndexOf("endstream", start, StringComparison.Ordinal);
            if (end < 0)
            {
                break;
            }

            int length = end - start;
            if (length > 2)
            {
                try
                {
                    using var ms = new MemoryStream(pdfBytes, start, length);
                    using var zlib = new ZLibStream(ms, CompressionMode.Decompress);
                    using var reader = new StreamReader(zlib, Encoding.Latin1);
                    sb.AppendLine(reader.ReadToEnd());
                }
                catch
                {
                    // Stream may not be zlib compressed
                }
            }

            idx = end + 9;
        }

        return sb.ToString();
    }
}
