using System;
using Npgsql;

namespace MedHubPrescriptionTool.Configuration;

/// <summary>
/// Encapsulates PostgreSQL connection parameters for MedHub database access.
/// Defaults are configured for local MedHub database (amisdbV2).
/// </summary>
public class DbConnectionSettings
{
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 5433;
    public string Database { get; set; } = "amisdbV2";
    public string Username { get; set; } = "postgres";
    public string Password { get; set; } = "Syscom@123";
    public int TimeoutSeconds { get; set; } = 15;
    public int CommandTimeoutSeconds { get; set; } = 30;
    public bool ReadOnly { get; set; } = true;
    public bool Pooling { get; set; } = true;
    public int MinPoolSize { get; set; } = 1;
    public int MaxPoolSize { get; set; } = 20;

    /// <summary>
    /// Generates an Npgsql connection string.
    /// When readOnly is true (the default), attaches the PostgreSQL server option
    /// "-c default_transaction_read_only=on" to enforce read-only at the protocol level.
    /// </summary>
    public string ToConnectionString(bool? readOnlyOverride = null)
    {
        bool enforceReadOnly = readOnlyOverride ?? ReadOnly;

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = string.IsNullOrWhiteSpace(Host) ? "127.0.0.1" : Host.Trim(),
            Port = Port > 0 ? Port : 5433,
            Database = string.IsNullOrWhiteSpace(Database) ? "amisdbV2" : Database.Trim(),
            Username = string.IsNullOrWhiteSpace(Username) ? "postgres" : Username.Trim(),
            Password = Password ?? string.Empty,
            Timeout = TimeoutSeconds > 0 ? TimeoutSeconds : 15,
            CommandTimeout = CommandTimeoutSeconds > 0 ? CommandTimeoutSeconds : 30,
            Pooling = Pooling,
            MinPoolSize = MinPoolSize >= 0 ? MinPoolSize : 1,
            MaxPoolSize = MaxPoolSize >= 1 ? MaxPoolSize : 20
        };

        if (enforceReadOnly)
        {
            builder.Options = "-c default_transaction_read_only=on";
        }

        return builder.ConnectionString;
    }

    /// <summary>
    /// Performs basic validation of connection parameters.
    /// </summary>
    public (bool IsValid, string? Error) Validate()
    {
        if (string.IsNullOrWhiteSpace(Host))
            return (false, "Database Host must not be empty.");
        if (Port is <= 0 or > 65535)
            return (false, $"Database Port '{Port}' is out of valid range (1-65535).");
        if (string.IsNullOrWhiteSpace(Database))
            return (false, "Database Name must not be empty.");
        if (string.IsNullOrWhiteSpace(Username))
            return (false, "Database Username must not be empty.");

        return (true, null);
    }

    /// <summary>
    /// Clones this configuration object.
    /// </summary>
    public DbConnectionSettings Clone() => new()
    {
        Host = Host,
        Port = Port,
        Database = Database,
        Username = Username,
        Password = Password,
        TimeoutSeconds = TimeoutSeconds,
        CommandTimeoutSeconds = CommandTimeoutSeconds,
        ReadOnly = ReadOnly,
        Pooling = Pooling,
        MinPoolSize = MinPoolSize,
        MaxPoolSize = MaxPoolSize
    };
}
