using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using MedHubPrescriptionTool.Configuration;
using Npgsql;

namespace MedHubPrescriptionTool.Data;

/// <summary>
/// Result of a PostgreSQL database connectivity test.
/// </summary>
public class ConnectionTestResult
{
    public bool Success { get; set; }
    public long LatencyMs { get; set; }
    public string? ServerVersion { get; set; }
    public string? DatabaseName { get; set; }
    public bool IsReadOnly { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime TestedAt { get; set; } = DateTime.UtcNow;

    public override string ToString()
    {
        if (Success)
        {
            return $"Success ({LatencyMs}ms) - DB: {DatabaseName}, Version: {ServerVersion}, ReadOnly: {IsReadOnly}";
        }
        return $"Failed ({LatencyMs}ms) - Error: {ErrorMessage}";
    }
}

/// <summary>
/// Contract for creating database connections and testing database reachability.
/// </summary>
public interface IDatabaseConnectionFactory
{
    DbConnectionSettings CurrentSettings { get; }
    void UpdateSettings(DbConnectionSettings newSettings);
    Task<NpgsqlConnection> CreateOpenConnectionAsync(bool readOnly = true, CancellationToken cancellationToken = default);
    Task<ConnectionTestResult> TestConnectionAsync(DbConnectionSettings? testSettings = null, CancellationToken cancellationToken = default);
}

/// <summary>
/// Factory that creates and initializes Npgsql connections with strict multi-layer
/// read-only enforcement:
/// 1. Protocol-level connection string option: Options=-c default_transaction_read_only=on
/// 2. Session-level initialization hook: SET transaction_read_only = on;
/// </summary>
public class DatabaseConnectionFactory : IDatabaseConnectionFactory
{
    private DbConnectionSettings _settings;

    public DatabaseConnectionFactory(DbConnectionSettings? settings = null)
    {
        _settings = settings ?? AppConfigurationManager.Current.Database;
    }

    public DbConnectionSettings CurrentSettings => _settings;

    public void UpdateSettings(DbConnectionSettings newSettings)
    {
        _settings = newSettings ?? throw new ArgumentNullException(nameof(newSettings));
    }

    /// <summary>
    /// Creates and opens a new PostgreSQL connection.
    /// When readOnly is true (default), enforces read-only mode both via connection
    /// options and an immediate session SET statement.
    /// </summary>
    public async Task<NpgsqlConnection> CreateOpenConnectionAsync(bool readOnly = true, CancellationToken cancellationToken = default)
    {
        string connStr = _settings.ToConnectionString(readOnlyOverride: readOnly);
        var connection = new NpgsqlConnection(connStr);

        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            if (readOnly)
            {
                // Multi-layer defense: Explicitly set session parameter
                await using var cmd = new NpgsqlCommand("SET transaction_read_only = on;", connection);
                await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            return connection;
        }
        catch (Exception ex)
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw new InvalidOperationException(
                $"Failed to connect to MedHub PostgreSQL database at {_settings.Host}:{_settings.Port}/{_settings.Database} (User: {_settings.Username}). Error: {ex.Message}",
                ex);
        }
    }

    /// <summary>
    /// Tests database reachability, latency, and read-only status without altering state.
    /// </summary>
    public async Task<ConnectionTestResult> TestConnectionAsync(DbConnectionSettings? testSettings = null, CancellationToken cancellationToken = default)
    {
        var settings = testSettings ?? _settings;
        var validation = settings.Validate();
        if (!validation.IsValid)
        {
            return new ConnectionTestResult
            {
                Success = false,
                ErrorMessage = validation.Error
            };
        }

        var sw = Stopwatch.StartNew();
        string connStr = settings.ToConnectionString(readOnlyOverride: true);

        try
        {
            await using var conn = new NpgsqlConnection(connStr);
            await conn.OpenAsync(cancellationToken).ConfigureAwait(false);

            // Execute read-only verification query
            await using var cmd = new NpgsqlCommand("SET transaction_read_only = on;", conn);
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            await using var queryCmd = new NpgsqlCommand("SELECT version(), current_database(), current_setting('transaction_read_only');", conn);
            await using var reader = await queryCmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                string version = reader.GetString(0);
                string dbName = reader.GetString(1);
                string roSetting = reader.GetString(2);
                sw.Stop();

                return new ConnectionTestResult
                {
                    Success = true,
                    LatencyMs = sw.ElapsedMilliseconds,
                    ServerVersion = version,
                    DatabaseName = dbName,
                    IsReadOnly = string.Equals(roSetting, "on", StringComparison.OrdinalIgnoreCase)
                };
            }

            sw.Stop();
            return new ConnectionTestResult
            {
                Success = false,
                LatencyMs = sw.ElapsedMilliseconds,
                ErrorMessage = "Database connected but query returned no metadata rows."
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new ConnectionTestResult
            {
                Success = false,
                LatencyMs = sw.ElapsedMilliseconds,
                ErrorMessage = ex.Message
            };
        }
    }
}
