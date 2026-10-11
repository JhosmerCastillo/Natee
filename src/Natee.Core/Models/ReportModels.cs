namespace Natee.Core.Models;

/// <summary>
/// Complete point-in-time technical report document used to generate human-readable PDF and machine-readable JSON exports.
/// </summary>
public sealed record TechnicalReportDocument
{
    public Guid ReportId { get; init; } = Guid.NewGuid();
    public DateTimeOffset GeneratedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public string NateeVersion { get; init; } = "1.0.0";
    public string Tagline { get; init; } = "Calm monitoring. Clear diagnostics.";
    public string SnapshotNatureDisclaimer { get; init; } =
        "This report represents a point-in-time diagnostic snapshot collected at the indicated timestamps and is not a continuous historical trace.";

    public PrivacyRedactionOptions AppliedPrivacyOptions { get; init; } = new();
    public IReadOnlyList<string> ExcludedFieldsSummary { get; init; } = Array.Empty<string>();

    public SystemMetricsSnapshot System { get; init; } = new();
    public NetworkSnapshot Network { get; init; } = new();
    public DiagnosticReport? LatestDiagnosis { get; init; }
    public SpeedTestResult? LatestSpeedTest { get; init; }
    public IReadOnlyList<AlertNotification> RecentAlerts { get; init; } = Array.Empty<AlertNotification>();
    public IReadOnlyList<string> ActiveWarnings { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> UnavailableMeasurements { get; init; } = Array.Empty<string>();
}
