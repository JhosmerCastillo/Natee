namespace Natee.Core.Models;

/// <summary>
/// Represents a single check executed during a One-Click Network Diagnosis session.
/// </summary>
public sealed record DiagnosticCheckItem
{
    public string Id { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public HealthStatus Status { get; init; } = HealthStatus.Inconclusive;
    public string WhatWasTested { get; init; } = string.Empty;
    public string MeasuredResult { get; init; } = string.Empty;
    public string WhyItMatters { get; init; } = string.Empty;
    public string TechnicalDetails { get; init; } = string.Empty;
    public double DurationMs { get; init; }
}

/// <summary>
/// Complete report of a One-Click Network Diagnosis session, including individual checks and evidence-based root-cause summary.
/// </summary>
public sealed record DiagnosticReport
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public DateTimeOffset StartedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset CompletedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public TimeSpan Duration => CompletedAtUtc - StartedAtUtc;
    public HealthStatus OverallStatus { get; init; } = HealthStatus.Inconclusive;
    public string SummaryTitle { get; init; } = string.Empty;
    public string SummaryExplanation { get; init; } = string.Empty;
    public IReadOnlyList<string> LikelyCauses { get; init; } = Array.Empty<string>();
    public string? UncertaintyNotice { get; init; }
    public IReadOnlyList<DiagnosticCheckItem> Checks { get; init; } = Array.Empty<DiagnosticCheckItem>();
}
