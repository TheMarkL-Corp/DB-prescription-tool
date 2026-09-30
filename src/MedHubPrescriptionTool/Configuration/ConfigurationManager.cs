using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MedHubPrescriptionTool.Configuration;

/// <summary>
/// Root application configuration model.
/// </summary>
public class AppConfig
{
    public DbConnectionSettings Database { get; set; } = new();
    public UiConfig Ui { get; set; } = new();
}

/// <summary>
/// User interface and client-side behavior configuration.
/// </summary>
public class UiConfig
{
    public string Theme { get; set; } = "clinical-light";
    public string DefaultStation { get; set; } = string.Empty;
    public int AutoRefreshIntervalSeconds { get; set; } = 0; // 0 = manual refresh only (per R2)
}

/// <summary>
/// Manages loading, caching, and persisting configuration from appsettings.json
/// adjacent to the application executable using AppContext.BaseDirectory.
/// Resilient against missing, unreadable, or corrupted files.
/// </summary>
public static class AppConfigurationManager
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    private static readonly object SyncLock = new();
    private static AppConfig? _current;

    /// <summary>
    /// Gets the current cached configuration, loading from disk if not yet loaded.
    /// </summary>
    public static AppConfig Current
    {
        get
        {
            if (_current == null)
            {
                lock (SyncLock)
                {
                    _current ??= Load();
                }
            }
            return _current;
        }
    }

    /// <summary>
    /// Resolves the absolute path to appsettings.json next to the executable.
    /// </summary>
    public static string GetConfigFilePath(string? customPath = null)
    {
        if (!string.IsNullOrWhiteSpace(customPath))
            return Path.GetFullPath(customPath);

        return Path.Combine(AppContext.BaseDirectory, "appsettings.json");
    }

    /// <summary>
    /// Loads configuration from appsettings.json.
    /// Returns default configuration if the file is absent or malformed.
    /// </summary>
    public static AppConfig Load(string? customPath = null)
    {
        string filePath = GetConfigFilePath(customPath);
        var config = new AppConfig();

        try
        {
            if (File.Exists(filePath))
            {
                string json = File.ReadAllText(filePath);
                var loaded = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions);
                if (loaded != null)
                {
                    config = loaded;
                }
            }
            else
            {
                // Auto-create default file if writable
                Save(config, filePath);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ConfigurationManager] Warning: Failed to load '{filePath}' ({ex.Message}). Falling back to defaults.");
        }

        lock (SyncLock)
        {
            _current = config;
        }

        return config;
    }

    /// <summary>
    /// Persists the given configuration to appsettings.json.
    /// </summary>
    public static bool Save(AppConfig config, string? customPath = null)
    {
        string filePath = GetConfigFilePath(customPath);
        try
        {
            string? dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            string json = JsonSerializer.Serialize(config, JsonOptions);
            File.WriteAllText(filePath, json);

            lock (SyncLock)
            {
                _current = config;
            }
            return true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ConfigurationManager] Warning: Failed to save '{filePath}' ({ex.Message}).");
            return false;
        }
    }

    /// <summary>
    /// Updates database connection settings in memory and optionally persists them to disk.
    /// </summary>
    public static bool UpdateDatabaseSettings(DbConnectionSettings newDbSettings, bool persist = true, string? customPath = null)
    {
        var config = Current;
        config.Database = newDbSettings;

        if (persist)
        {
            return Save(config, customPath);
        }

        lock (SyncLock)
        {
            _current = config;
        }
        return true;
    }
}
