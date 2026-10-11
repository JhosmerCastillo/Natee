using System.Text.Json;
using System.Text.Json.Serialization;
using Natee.Core.Interfaces;
using Natee.Core.Models;
using SkiaSharp;

namespace Natee.Infrastructure.Reporting;

/// <summary>
/// Generates human-readable PDF reports (via SkiaSharp PDF document engine) and machine-readable JSON reports
/// from privacy-redacted <see cref="TechnicalReportDocument"/> snapshots.
/// </summary>
public sealed class ReportGeneratorService : IReportGeneratorService
{
    private static readonly JsonSerializerOptions JsonExportOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<string> GenerateJsonReportAsync(
        TechnicalReportDocument redactedDocument,
        string outputFilePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(redactedDocument);
        EnsureParentDirectory(outputFilePath);

        string json = JsonSerializer.Serialize(redactedDocument, JsonExportOptions);
        await File.WriteAllTextAsync(outputFilePath, json, cancellationToken);
        return json;
    }

    public Task GeneratePdfReportAsync(
        TechnicalReportDocument doc,
        string outputFilePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(doc);
        EnsureParentDirectory(outputFilePath);

        // Privacy-safe PDF metadata (never embeds raw username or hostname)
        var metadata = new SKDocumentPdfMetadata
        {
            Title = "Natee Technical Diagnostic Report",
            Author = "Natee Diagnostics",
            Creator = $"Natee v{doc.NateeVersion}",
            Subject = "Network and System Diagnostic Snapshot"
        };

        using var fileStream = new FileStream(outputFilePath, FileMode.Create, FileAccess.Write, FileShare.None);
        using var pdfDocument = SKDocument.CreatePdf(fileStream, metadata);

        const float pageWidth = 595f;  // A4 width in points
        const float pageHeight = 842f; // A4 height in points
        const float margin = 40f;
        const float contentWidth = pageWidth - (margin * 2);

        using var titleFont = new SKFont(SKTypeface.Default, 18f);
        using var subtitleFont = new SKFont(SKTypeface.Default, 10f);
        using var sectionFont = new SKFont(SKTypeface.Default, 13f);
        using var bodyFont = new SKFont(SKTypeface.Default, 9.5f);
        using var smallFont = new SKFont(SKTypeface.Default, 8.5f);

        using var bannerPaint = new SKPaint { Color = new SKColor(15, 39, 56), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var tealAccentPaint = new SKPaint { Color = new SKColor(38, 166, 154), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var whitePaint = new SKPaint { Color = SKColors.White, IsAntialias = true, TextSize = 18f };
        using var mutedWhitePaint = new SKPaint { Color = new SKColor(200, 225, 235), IsAntialias = true, TextSize = 10f };
        using var darkTextPaint = new SKPaint { Color = new SKColor(25, 35, 45), IsAntialias = true, TextSize = 9.5f };
        using var mutedTextPaint = new SKPaint { Color = new SKColor(90, 105, 120), IsAntialias = true, TextSize = 9.5f };
        using var dividerPaint = new SKPaint { Color = new SKColor(220, 228, 235), StrokeWidth = 1f, IsAntialias = true };

        SKCanvas? canvas = null;
        float y = 0f;
        int pageNumber = 0;

        void StartNewPage()
        {
            if (canvas is not null)
            {
                DrawFooter(canvas, pageNumber, doc, pageWidth, pageHeight, margin, smallFont, mutedTextPaint);
                pdfDocument.EndPage();
            }

            pageNumber++;
            canvas = pdfDocument.BeginPage(pageWidth, pageHeight);
            y = margin;

            if (pageNumber == 1)
            {
                // Header banner on Page 1
                var bannerRect = new SKRect(margin, y, pageWidth - margin, y + 66f);
                canvas.DrawRoundRect(bannerRect, 6f, 6f, bannerPaint);
                canvas.DrawCircle(margin + 28f, y + 33f, 14f, tealAccentPaint);

                canvas.DrawText("NATEE — Technical Diagnostic Report", margin + 52f, y + 28f, titleFont, whitePaint);
                canvas.DrawText($"{doc.Tagline} | Version {doc.NateeVersion} | Generated: {doc.GeneratedAtUtc:yyyy-MM-dd HH:mm:ss 'UTC'}",
                    margin + 52f, y + 46f, subtitleFont, mutedWhitePaint);
                y += 80f;

                DrawWrappedText(doc.SnapshotNatureDisclaimer, smallFont, mutedTextPaint, contentWidth);
                y += 8f;
            }
        }

        void EnsureSpace(float neededHeight)
        {
            if (canvas is null || y + neededHeight > pageHeight - margin - 28f)
            {
                StartNewPage();
            }
        }

        void DrawSectionHeader(string heading)
        {
            EnsureSpace(32f);
            y += 6f;
            canvas!.DrawRect(new SKRect(margin, y - 10f, margin + 4f, y + 4f), tealAccentPaint);
            canvas.DrawText(heading, margin + 10f, y + 2f, sectionFont, darkTextPaint);
            y += 10f;
            canvas.DrawLine(margin, y, pageWidth - margin, y, dividerPaint);
            y += 14f;
        }

        void DrawKeyValue(string label, string value)
        {
            EnsureSpace(16f);
            canvas!.DrawText(label + ":", margin + 6f, y, bodyFont, mutedTextPaint);
            DrawWrappedTextAtX(value, margin + 165f, contentWidth - 165f, bodyFont, darkTextPaint);
        }

        void DrawWrappedText(string text, SKFont font, SKPaint paint, float maxWidth)
        {
            DrawWrappedTextAtX(text, margin + 6f, maxWidth - 6f, font, paint);
        }

        void DrawWrappedTextAtX(string text, float x, float maxWidth, SKFont font, SKPaint paint)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                y += 14f;
                return;
            }

            paint.TextSize = font.Size;
            string[] words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var currentLine = new System.Text.StringBuilder();

            foreach (string word in words)
            {
                string candidate = currentLine.Length == 0 ? word : $"{currentLine} {word}";
                if (paint.MeasureText(candidate) > maxWidth && currentLine.Length > 0)
                {
                    EnsureSpace(14f);
                    canvas!.DrawText(currentLine.ToString(), x, y, font, paint);
                    y += 13f;
                    currentLine.Clear();
                    currentLine.Append(word);
                }
                else
                {
                    currentLine.Clear();
                    currentLine.Append(candidate);
                }
            }

            if (currentLine.Length > 0)
            {
                EnsureSpace(14f);
                canvas!.DrawText(currentLine.ToString(), x, y, font, paint);
                y += 14f;
            }
        }

        StartNewPage();

        // 1. Privacy & Redaction Summary
        DrawSectionHeader("1. Privacy & Redaction Controls Applied");
        if (doc.ExcludedFieldsSummary.Count == 0)
        {
            DrawWrappedText("No privacy redactions were enabled; full diagnostic identifiers are included.", bodyFont, darkTextPaint, contentWidth);
        }
        else
        {
            foreach (string item in doc.ExcludedFieldsSummary)
            {
                DrawWrappedText($"• {item}", bodyFont, darkTextPaint, contentWidth);
            }
        }

        // 2. Network Connectivity & Configuration
        DrawSectionHeader("2. Network Connectivity & Configuration");
        var net = doc.Network;
        DrawKeyValue("Connection Status", $"{net.ConnectionStatus} — {net.ConnectionStatusSummary}");
        DrawKeyValue("Latest Connectivity Check", net.LatestCheckTimestampUtc.ToString("yyyy-MM-dd HH:mm:ss 'UTC'"));
        DrawKeyValue("Active Interface", $"{net.ActiveInterfaceName} ({net.ConnectionType})");
        DrawKeyValue("Interface ID", string.IsNullOrWhiteSpace(net.ActiveInterfaceId) ? "None" : net.ActiveInterfaceId);
        DrawKeyValue("MAC Address", net.MacAddress ?? "Unavailable");
        DrawKeyValue("Wi-Fi SSID", net.WifiSsid ?? "Not applicable / Unavailable");
        DrawKeyValue("Local IPv4 Address", net.Ipv4Address ?? "None");
        DrawKeyValue("Local IPv6 Address", net.Ipv6Address ?? "None");
        DrawKeyValue("Default Gateway", net.DefaultGateway ?? "None");
        DrawKeyValue("Configured DNS Servers", net.DnsServers.Count > 0 ? string.Join(", ", net.DnsServers) : "None");
        DrawKeyValue("Live Observed Throughput", $"Download: {net.DownloadThroughputMbps:F2} Mbps | Upload: {net.UploadThroughputMbps:F2} Mbps (observed traffic)");
        DrawKeyValue("Measured Latency", net.LatencyMs.HasValue ? $"{net.LatencyMs.Value:F1} ms" : "Unavailable");
        DrawKeyValue("Packet Loss", $"{net.PacketLossPercent:F1}% ({net.ProbeTargetSummary})");

        // 3. System & Hardware Summary
        DrawSectionHeader("3. System Resources & Hardware Condition");
        var sys = doc.System;
        DrawKeyValue("Operating System", sys.OsDescription);
        DrawKeyValue("Computer Hostname", sys.Hostname);
        DrawKeyValue("Active User", sys.Username);
        DrawKeyValue("System Uptime", sys.FormattedUptime);
        DrawKeyValue("Processor", $"{sys.CpuModel} ({sys.LogicalCores} logical cores)");
        DrawKeyValue("CPU Utilization", $"{sys.CpuUsagePercent:F1}%");
        DrawKeyValue("CPU Operating Frequency", sys.CpuFrequencyMHz.HasValue ? $"{sys.CpuFrequencyMHz.Value:F0} MHz" : "Unavailable");
        DrawKeyValue("CPU Temperature", sys.CpuTemperatureCelsius.HasValue
            ? $"{sys.CpuTemperatureCelsius.Value:F1} °C ({sys.TemperatureSensorNote})"
            : $"Unavailable ({sys.TemperatureSensorStatus}: {sys.TemperatureSensorNote})");
        DrawKeyValue("Physical RAM", $"{sys.UsedRamGigabytes:F2} GB used / {sys.TotalRamGigabytes:F2} GB total ({sys.RamUsagePercent:F1}%, {sys.AvailableRamGigabytes:F2} GB available)");

        // 4. Storage Volumes
        DrawSectionHeader("4. Storage Volumes");
        if (sys.Volumes.Count == 0)
        {
            DrawWrappedText("No storage volumes reported.", bodyFont, mutedTextPaint, contentWidth);
        }
        else
        {
            foreach (var vol in sys.Volumes)
            {
                if (vol.IsAccessible)
                {
                    string ioNote = (vol.ReadThroughputMbps.HasValue || vol.WriteThroughputMbps.HasValue)
                        ? $" | Read: {vol.ReadThroughputMbps ?? 0:F2} Mbps, Write: {vol.WriteThroughputMbps ?? 0:F2} Mbps"
                        : string.Empty;
                    DrawWrappedText(
                        $"• {vol.VolumeId} [{vol.MountPoint}] ({vol.FileSystemType}): {vol.UsedGigabytes:F2} GB used / {vol.TotalGigabytes:F2} GB total ({vol.UsagePercent:F1}% used, {vol.AvailableGigabytes:F2} GB free){ioNote}",
                        bodyFont, darkTextPaint, contentWidth);
                }
                else
                {
                    DrawWrappedText(
                        $"• {vol.VolumeId} [{vol.MountPoint}]: Unavailable ({vol.StatusNote})",
                        bodyFont, mutedTextPaint, contentWidth);
                }
            }
        }

        // 5. Top Resource Processes
        DrawSectionHeader("5. Top Resource-Consuming Processes");
        if (sys.TopProcesses.Count == 0)
        {
            DrawWrappedText("No process metrics available.", bodyFont, mutedTextPaint, contentWidth);
        }
        else
        {
            foreach (var proc in sys.TopProcesses)
            {
                DrawWrappedText(
                    $"• {proc.ProcessName} (PID: {proc.ProcessId}): CPU {proc.CpuUsagePercent:F1}% | RAM {proc.MemoryMegabytes:F1} MB",
                    bodyFont, darkTextPaint, contentWidth);
            }
        }

        // 6. One-Click Network Diagnosis
        DrawSectionHeader("6. One-Click Network Diagnosis Results");
        if (doc.LatestDiagnosis is null)
        {
            DrawWrappedText("One-Click Network Diagnosis has not been executed in this session.", bodyFont, mutedTextPaint, contentWidth);
        }
        else
        {
            var diag = doc.LatestDiagnosis;
            DrawKeyValue("Overall Status", $"{diag.OverallStatus} — {diag.SummaryTitle}");
            DrawKeyValue("Executed At", $"{diag.CompletedAtUtc:yyyy-MM-dd HH:mm:ss 'UTC'} (Duration: {diag.Duration.TotalMilliseconds:F0} ms)");
            DrawKeyValue("Summary", diag.SummaryExplanation);

            if (diag.LikelyCauses.Count > 0)
            {
                DrawKeyValue("Likely Causes", string.Join("; ", diag.LikelyCauses));
            }

            if (!string.IsNullOrWhiteSpace(diag.UncertaintyNotice))
            {
                DrawKeyValue("Diagnostic Qualification", diag.UncertaintyNotice);
            }

            y += 4f;
            foreach (var check in diag.Checks)
            {
                EnsureSpace(42f);
                DrawWrappedText(
                    $"[{check.Status.ToString().ToUpperInvariant()}] {check.Title}: {check.MeasuredResult} ({check.DurationMs:F0} ms)",
                    bodyFont, darkTextPaint, contentWidth);
                DrawWrappedText(
                    $"    Tested: {check.WhatWasTested} | Details: {check.TechnicalDetails}",
                    smallFont, mutedTextPaint, contentWidth);
            }
        }

        // 7. Internet Speed Test
        DrawSectionHeader("7. User-Initiated Internet Speed Test");
        if (doc.LatestSpeedTest is null)
        {
            DrawWrappedText("No manual speed test was executed in this session (Natee never runs speed tests automatically).", bodyFont, mutedTextPaint, contentWidth);
        }
        else
        {
            var st = doc.LatestSpeedTest;
            DrawKeyValue("Provider & Timestamp", $"{st.ProviderName} at {st.TimestampUtc:yyyy-MM-dd HH:mm:ss 'UTC'} (Duration: {st.Duration.TotalSeconds:F1} s)");
            DrawKeyValue("Status", st.IsSuccess ? "Completed Successfully" : $"Failed ({st.ErrorMessage})");
            DrawKeyValue("Measured Download", $"{st.DownloadSpeedMbps:F2} Mbps ({st.BytesDownloaded / (1024.0 * 1024.0):F1} MB transferred)");
            DrawKeyValue("Measured Upload", $"{st.UploadSpeedMbps:F2} Mbps ({st.BytesUploaded / (1024.0 * 1024.0):F1} MB transferred)");
            DrawKeyValue("Methodology & Limits", st.LimitationsNotice);
        }

        // 8. Active Warnings & Unavailable Measurements
        DrawSectionHeader("8. Active Warnings & Unavailable Measurements");
        if (doc.ActiveWarnings.Count == 0)
        {
            DrawWrappedText("Active Warnings: None detected.", bodyFont, darkTextPaint, contentWidth);
        }
        else
        {
            foreach (string warn in doc.ActiveWarnings)
            {
                DrawWrappedText($"• [WARNING] {warn}", bodyFont, darkTextPaint, contentWidth);
            }
        }

        y += 4f;
        if (doc.UnavailableMeasurements.Count == 0)
        {
            DrawWrappedText("Unavailable Measurements: All standard hardware and OS sensors responded.", bodyFont, mutedTextPaint, contentWidth);
        }
        else
        {
            foreach (string unavail in doc.UnavailableMeasurements)
            {
                DrawWrappedText($"• [UNAVAILABLE] {unavail}", smallFont, mutedTextPaint, contentWidth);
            }
        }

        if (canvas is not null)
        {
            DrawFooter(canvas, pageNumber, doc, pageWidth, pageHeight, margin, smallFont, mutedTextPaint);
            pdfDocument.EndPage();
        }

        pdfDocument.Close();
        return Task.CompletedTask;
    }

    private static void DrawFooter(
        SKCanvas canvas,
        int pageNumber,
        TechnicalReportDocument doc,
        float pageWidth,
        float pageHeight,
        float margin,
        SKFont font,
        SKPaint paint)
    {
        paint.TextSize = font.Size;
        string left = $"Natee v{doc.NateeVersion} — Report ID: {doc.ReportId}";
        string right = $"Page {pageNumber}";
        canvas.DrawText(left, margin, pageHeight - 20f, font, paint);
        float rightWidth = paint.MeasureText(right);
        canvas.DrawText(right, pageWidth - margin - rightWidth, pageHeight - 20f, font, paint);
    }

    private static void EnsureParentDirectory(string filePath)
    {
        string? dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }
    }
}
