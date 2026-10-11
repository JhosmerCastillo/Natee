using System.Text.Json;
using Microsoft.Data.Sqlite;
using Natee.Core.Interfaces;
using Natee.Core.Models;
using Natee.Infrastructure.Platform;

namespace Natee.Infrastructure.Persistence;

/// <summary>
/// Manages local SQLite persistence for diagnostic reports, speed test records, and alert notifications.
/// Includes versioned schema migrations, automatic retention pruning, and complete history deletion.
/// </summary>
public sealed class SqlitePersistenceService : ILocalPersistenceService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly string _connectionString;
    public string DatabaseFilePath { get; }

    public SqlitePersistenceService(string? customDatabaseFilePath = null)
    {
        DatabaseFilePath = customDatabaseFilePath ?? AppDataPaths.GetDatabaseFilePath();
        string? parentDir = Path.GetDirectoryName(DatabaseFilePath);
        if (!string.IsNullOrWhiteSpace(parentDir))
        {
            Directory.CreateDirectory(parentDir);
        }

        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = DatabaseFilePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = true
        };
        _connectionString = builder.ToString();
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using (var pragmaCmd = connection.CreateCommand())
        {
            pragmaCmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL;";
            await pragmaCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var migTableCmd = connection.CreateCommand())
        {
            migTableCmd.CommandText = """
                CREATE TABLE IF NOT EXISTS schema_migrations (
                    version INTEGER PRIMARY KEY NOT NULL,
                    applied_at_utc TEXT NOT NULL,
                    description TEXT NOT NULL
                );
                """;
            await migTableCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        int currentVersion = await GetCurrentSchemaVersionAsync(connection, cancellationToken);

        if (currentVersion < 1)
        {
            await using var tx = await connection.BeginTransactionAsync(cancellationToken);
            await using var cmd = connection.CreateCommand();
            cmd.Transaction = (SqliteTransaction)tx;
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS diagnostic_reports (
                    id TEXT PRIMARY KEY NOT NULL,
                    completed_at_utc TEXT NOT NULL,
                    overall_status INTEGER NOT NULL,
                    summary_title TEXT NOT NULL,
                    payload_json TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS ix_diagnostic_reports_completed ON diagnostic_reports(completed_at_utc DESC);

                CREATE TABLE IF NOT EXISTS speed_test_results (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    timestamp_utc TEXT NOT NULL,
                    provider_name TEXT NOT NULL,
                    download_mbps REAL NOT NULL,
                    upload_mbps REAL NOT NULL,
                    is_success INTEGER NOT NULL,
                    payload_json TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS ix_speed_test_results_ts ON speed_test_results(timestamp_utc DESC);

                CREATE TABLE IF NOT EXISTS alert_notifications (
                    id TEXT PRIMARY KEY NOT NULL,
                    timestamp_utc TEXT NOT NULL,
                    alert_type INTEGER NOT NULL,
                    severity INTEGER NOT NULL,
                    title TEXT NOT NULL,
                    message TEXT NOT NULL,
                    is_recovery INTEGER NOT NULL
                );
                CREATE INDEX IF NOT EXISTS ix_alert_notifications_ts ON alert_notifications(timestamp_utc DESC);

                INSERT INTO schema_migrations (version, applied_at_utc, description)
                VALUES (1, $now, 'Initial Natee MVP schema for diagnostics, speed tests, and alerts');
                """;
            cmd.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
            await cmd.ExecuteNonQueryAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
        }
    }

    public async Task<int> GetSchemaVersionAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return await GetCurrentSchemaVersionAsync(connection, cancellationToken);
    }

    private static async Task<int> GetCurrentSchemaVersionAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COALESCE(MAX(version), 0) FROM schema_migrations;";
        object? scalar = await cmd.ExecuteScalarAsync(cancellationToken);
        return scalar is long v ? (int)v : 0;
    }

    public async Task SaveDiagnosticReportAsync(DiagnosticReport report, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(report);
        string json = JsonSerializer.Serialize(report, JsonOptions);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT OR REPLACE INTO diagnostic_reports (id, completed_at_utc, overall_status, summary_title, payload_json)
            VALUES ($id, $completed, $status, $title, $json);
            """;
        cmd.Parameters.AddWithValue("$id", report.Id.ToString());
        cmd.Parameters.AddWithValue("$completed", report.CompletedAtUtc.ToString("O"));
        cmd.Parameters.AddWithValue("$status", (int)report.OverallStatus);
        cmd.Parameters.AddWithValue("$title", report.SummaryTitle);
        cmd.Parameters.AddWithValue("$json", json);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SaveSpeedTestResultAsync(SpeedTestResult result, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);
        string json = JsonSerializer.Serialize(result, JsonOptions);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO speed_test_results (timestamp_utc, provider_name, download_mbps, upload_mbps, is_success, payload_json)
            VALUES ($ts, $provider, $dl, $ul, $ok, $json);
            """;
        cmd.Parameters.AddWithValue("$ts", result.TimestampUtc.ToString("O"));
        cmd.Parameters.AddWithValue("$provider", result.ProviderName);
        cmd.Parameters.AddWithValue("$dl", result.DownloadSpeedMbps);
        cmd.Parameters.AddWithValue("$ul", result.UploadSpeedMbps);
        cmd.Parameters.AddWithValue("$ok", result.IsSuccess ? 1 : 0);
        cmd.Parameters.AddWithValue("$json", json);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SaveAlertAsync(AlertNotification alert, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(alert);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT OR REPLACE INTO alert_notifications (id, timestamp_utc, alert_type, severity, title, message, is_recovery)
            VALUES ($id, $ts, $type, $sev, $title, $msg, $rec);
            """;
        cmd.Parameters.AddWithValue("$id", alert.Id.ToString());
        cmd.Parameters.AddWithValue("$ts", alert.TimestampUtc.ToString("O"));
        cmd.Parameters.AddWithValue("$type", (int)alert.AlertType);
        cmd.Parameters.AddWithValue("$sev", (int)alert.Severity);
        cmd.Parameters.AddWithValue("$title", alert.Title);
        cmd.Parameters.AddWithValue("$msg", alert.Message);
        cmd.Parameters.AddWithValue("$rec", alert.IsRecovery ? 1 : 0);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DiagnosticReport>> GetRecentDiagnosticsAsync(
        int limit = 25,
        CancellationToken cancellationToken = default)
    {
        var list = new List<DiagnosticReport>();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT payload_json FROM diagnostic_reports ORDER BY completed_at_utc DESC LIMIT $limit;";
        cmd.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 200));

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            string json = reader.GetString(0);
            var item = JsonSerializer.Deserialize<DiagnosticReport>(json, JsonOptions);
            if (item is not null)
            {
                list.Add(item);
            }
        }

        return list;
    }

    public async Task<IReadOnlyList<SpeedTestResult>> GetRecentSpeedTestsAsync(
        int limit = 25,
        CancellationToken cancellationToken = default)
    {
        var list = new List<SpeedTestResult>();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT payload_json FROM speed_test_results ORDER BY timestamp_utc DESC LIMIT $limit;";
        cmd.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 200));

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            string json = reader.GetString(0);
            var item = JsonSerializer.Deserialize<SpeedTestResult>(json, JsonOptions);
            if (item is not null)
            {
                list.Add(item);
            }
        }

        return list;
    }

    public async Task<IReadOnlyList<AlertNotification>> GetRecentAlertsAsync(
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        var list = new List<AlertNotification>();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, timestamp_utc, alert_type, severity, title, message, is_recovery
            FROM alert_notifications
            ORDER BY timestamp_utc DESC
            LIMIT $limit;
            """;
        cmd.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 200));

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new AlertNotification
            {
                Id = Guid.TryParse(reader.GetString(0), out var id) ? id : Guid.NewGuid(),
                TimestampUtc = DateTimeOffset.TryParse(reader.GetString(1), out var ts) ? ts : DateTimeOffset.UtcNow,
                AlertType = (AlertType)reader.GetInt32(2),
                Severity = (HealthStatus)reader.GetInt32(3),
                Title = reader.GetString(4),
                Message = reader.GetString(5),
                IsRecovery = reader.GetInt32(6) != 0
            });
        }

        return list;
    }

    public async Task PruneExpiredHistoryAsync(int retentionDays, CancellationToken cancellationToken = default)
    {
        int clampedDays = Math.Clamp(retentionDays, 1, 90);
        string cutoffIso = DateTimeOffset.UtcNow.AddDays(-clampedDays).ToString("O");

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            DELETE FROM diagnostic_reports WHERE completed_at_utc < $cutoff;
            DELETE FROM speed_test_results WHERE timestamp_utc < $cutoff;
            DELETE FROM alert_notifications WHERE timestamp_utc < $cutoff;
            """;
        cmd.Parameters.AddWithValue("$cutoff", cutoffIso);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task ClearAllHistoryAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            DELETE FROM diagnostic_reports;
            DELETE FROM speed_test_results;
            DELETE FROM alert_notifications;
            VACUUM;
            """;
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }
}
