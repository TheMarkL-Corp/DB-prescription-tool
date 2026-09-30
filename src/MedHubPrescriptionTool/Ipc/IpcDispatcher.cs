using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using MedHubPrescriptionTool.Configuration;
using MedHubPrescriptionTool.Data;
using MedHubPrescriptionTool.Models;
using MedHubPrescriptionTool.Services;

namespace MedHubPrescriptionTool.Ipc;

/// <summary>
/// Dispatches IPC messages received from the web frontend to appropriate backend services.
/// </summary>
public class IpcDispatcher
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private IClinicalDataService _clinicalService;
    private IDatabaseRepository _repository;
    private readonly IExcelExportService _excelService;
    private ClinicalDataSnapshot? _cachedSnapshot;

    public IpcDispatcher(
        IClinicalDataService clinicalService,
        IDatabaseRepository repository,
        IExcelExportService excelService)
    {
        _clinicalService = clinicalService ?? throw new ArgumentNullException(nameof(clinicalService));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _excelService = excelService ?? throw new ArgumentNullException(nameof(excelService));
    }

    /// <summary>
    /// Creates a default IpcDispatcher wired with standard application configuration.
    /// </summary>
    public static IpcDispatcher CreateDefault()
    {
        var config = AppConfigurationManager.Current;
        var factory = new DatabaseConnectionFactory(config.Database);
        var repository = new DatabaseRepository(factory);
        var clinicalService = new ClinicalDataService(repository);
        var excelService = new ExcelExportService();

        return new IpcDispatcher(clinicalService, repository, excelService);
    }

    /// <summary>
    /// Processes a raw JSON request message from Photino and returns a serialized response string.
    /// </summary>
    public async Task<string> HandleMessageAsync(string rawJson, CancellationToken cancellationToken = default)
    {
        string requestId = "req_unknown";
        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            var root = doc.RootElement;

            if (root.TryGetProperty("requestId", out var reqProp))
            {
                requestId = reqProp.GetString() ?? requestId;
            }

            string action = string.Empty;
            if (root.TryGetProperty("action", out var actProp))
            {
                action = actProp.GetString() ?? string.Empty;
            }

            JsonElement? payload = null;
            if (root.TryGetProperty("payload", out var payloadProp) && payloadProp.ValueKind != JsonValueKind.Null && payloadProp.ValueKind != JsonValueKind.Undefined)
            {
                payload = payloadProp;
            }

            var response = await ExecuteActionAsync(action, payload, requestId, cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Serialize(response, JsonOptions);
        }
        catch (Exception ex)
        {
            var errResponse = IpcResponse.Fail(requestId, ex.Message);
            return JsonSerializer.Serialize(errResponse, JsonOptions);
        }
    }

    /// <summary>
    /// Executes a specific named IPC action.
    /// </summary>
    public async Task<IpcResponse> ExecuteActionAsync(
        string action,
        JsonElement? payload,
        string requestId = "test_req",
        CancellationToken cancellationToken = default)
    {
        try
        {
            switch (action)
            {
                case "loadSnapshot":
                    return await HandleLoadSnapshotAsync(requestId, cancellationToken).ConfigureAwait(false);

                case "refreshData":
                    return await HandleRefreshDataAsync(requestId, cancellationToken).ConfigureAwait(false);

                case "filterMar":
                    return await HandleFilterMarAsync(requestId, payload, cancellationToken).ConfigureAwait(false);

                case "getMedicationDetail":
                    return await HandleGetMedicationDetailAsync(requestId, payload, cancellationToken).ConfigureAwait(false);

                case "testDbConnection":
                    return await HandleTestDbConnectionAsync(requestId, payload, cancellationToken).ConfigureAwait(false);

                case "saveDbSettings":
                    return await HandleSaveDbSettingsAsync(requestId, payload, cancellationToken).ConfigureAwait(false);

                case "getAppSettings":
                    return HandleGetAppSettings(requestId);

                case "exportExcel":
                    return await HandleExportExcelAsync(requestId, payload, cancellationToken).ConfigureAwait(false);

                default:
                    return IpcResponse.Fail(requestId, $"Unrecognized IPC action: '{action}'");
            }
        }
        catch (Exception ex)
        {
            return IpcResponse.Fail(requestId, ex.Message);
        }
    }

    private async Task<ClinicalDataSnapshot> EnsureSnapshotLoadedAsync(CancellationToken cancellationToken)
    {
        if (_cachedSnapshot == null)
        {
            _cachedSnapshot = await _clinicalService.LoadClinicalSnapshotAsync(cancellationToken).ConfigureAwait(false);
        }
        return _cachedSnapshot;
    }

    private async Task<IpcResponse> HandleLoadSnapshotAsync(string requestId, CancellationToken cancellationToken)
    {
        var snapshot = await EnsureSnapshotLoadedAsync(cancellationToken).ConfigureAwait(false);
        return IpcResponse.Ok(requestId, snapshot);
    }

    private async Task<IpcResponse> HandleRefreshDataAsync(string requestId, CancellationToken cancellationToken)
    {
        _cachedSnapshot = await _clinicalService.LoadClinicalSnapshotAsync(cancellationToken).ConfigureAwait(false);
        return IpcResponse.Ok(requestId, _cachedSnapshot);
    }

    private async Task<IpcResponse> HandleFilterMarAsync(string requestId, JsonElement? payload, CancellationToken cancellationToken)
    {
        var snapshot = await EnsureSnapshotLoadedAsync(cancellationToken).ConfigureAwait(false);

        ClinicalFilterCriteria criteria = new();
        if (payload.HasValue)
        {
            var parsed = JsonSerializer.Deserialize<ClinicalFilterCriteria>(payload.Value.GetRawText(), JsonOptions);
            if (parsed != null) criteria = parsed;

            if (payload.Value.TryGetProperty("search", out var searchProp))
            {
                criteria.SearchQuery = searchProp.GetString();
            }
            if (payload.Value.TryGetProperty("station", out var stationProp))
            {
                criteria.StationCode = stationProp.GetString();
            }
            if (payload.Value.TryGetProperty("status", out var statusProp))
            {
                criteria.StatusFilter = statusProp.GetString();
            }
            if (payload.Value.TryGetProperty("highAlertOnly", out var haProp))
            {
                criteria.HighAlertOnly = haProp.GetBoolean();
            }
        }

        var results = _clinicalService.FilterMarSchedule(snapshot, criteria);
        return IpcResponse.Ok(requestId, results);
    }

    private async Task<IpcResponse> HandleGetMedicationDetailAsync(string requestId, JsonElement? payload, CancellationToken cancellationToken)
    {
        var snapshot = await EnsureSnapshotLoadedAsync(cancellationToken).ConfigureAwait(false);

        string itemCode = string.Empty;
        if (payload.HasValue)
        {
            if (payload.Value.ValueKind == JsonValueKind.String)
            {
                itemCode = payload.Value.GetString() ?? string.Empty;
            }
            else if (payload.Value.TryGetProperty("itemCode", out var codeProp))
            {
                itemCode = codeProp.GetString() ?? string.Empty;
            }
        }

        var detail = _clinicalService.GetMedicationDetails(snapshot, itemCode);
        return IpcResponse.Ok(requestId, detail);
    }

    private async Task<IpcResponse> HandleTestDbConnectionAsync(string requestId, JsonElement? payload, CancellationToken cancellationToken)
    {
        DbConnectionSettings settings = AppConfigurationManager.Current.Database.Clone();

        if (payload.HasValue)
        {
            var p = JsonSerializer.Deserialize<DbSettingsPayload>(payload.Value.GetRawText(), JsonOptions);
            if (p != null)
            {
                if (!string.IsNullOrWhiteSpace(p.Host)) settings.Host = p.Host;
                if (p.Port.HasValue && p.Port > 0) settings.Port = p.Port.Value;
                if (!string.IsNullOrWhiteSpace(p.Database)) settings.Database = p.Database;
                if (!string.IsNullOrWhiteSpace(p.Username)) settings.Username = p.Username;
                if (!string.IsNullOrWhiteSpace(p.Password)) settings.Password = p.Password;
            }
        }

        bool success = await _repository.TestConnectionAsync(settings).ConfigureAwait(false);
        return IpcResponse.Ok(requestId, new
        {
            success = success,
            message = success ? "Connection successful" : "Failed to connect to database"
        });
    }

    private async Task<IpcResponse> HandleSaveDbSettingsAsync(string requestId, JsonElement? payload, CancellationToken cancellationToken)
    {
        if (!payload.HasValue)
        {
            return IpcResponse.Fail(requestId, "Payload is required to save DB settings.");
        }

        var p = JsonSerializer.Deserialize<DbSettingsPayload>(payload.Value.GetRawText(), JsonOptions);
        if (p == null)
        {
            return IpcResponse.Fail(requestId, "Invalid DB settings payload.");
        }

        var settings = AppConfigurationManager.Current.Database.Clone();
        if (!string.IsNullOrWhiteSpace(p.Host)) settings.Host = p.Host;
        if (p.Port.HasValue && p.Port > 0) settings.Port = p.Port.Value;
        if (!string.IsNullOrWhiteSpace(p.Database)) settings.Database = p.Database;
        if (!string.IsNullOrWhiteSpace(p.Username)) settings.Username = p.Username;
        if (!string.IsNullOrWhiteSpace(p.Password)) settings.Password = p.Password;

        // Test connectivity first before persisting
        bool canConnect = await _repository.TestConnectionAsync(settings).ConfigureAwait(false);
        if (!canConnect)
        {
            return new IpcResponse
            {
                RequestId = requestId,
                Action = "saveDbSettings",
                Success = false,
                Error = "Database connection test failed. Settings were not saved."
            };
        }

        // Save settings to configuration
        bool saved = AppConfigurationManager.UpdateDatabaseSettings(settings, persist: true);
        if (!saved)
        {
            return IpcResponse.Fail(requestId, "Failed to persist appsettings.json.");
        }

        // Reconnect repository and reload snapshot
        var factory = new DatabaseConnectionFactory(settings);
        _repository = new DatabaseRepository(factory);
        _clinicalService = new ClinicalDataService(_repository);
        _cachedSnapshot = await _clinicalService.LoadClinicalSnapshotAsync(cancellationToken).ConfigureAwait(false);

        return IpcResponse.Ok(requestId, new
        {
            saved = true,
            snapshot = _cachedSnapshot
        });
    }

    private IpcResponse HandleGetAppSettings(string requestId)
    {
        var config = AppConfigurationManager.Current;
        return IpcResponse.Ok(requestId, new
        {
            host = config.Database.Host,
            port = config.Database.Port,
            database = config.Database.Database,
            username = config.Database.Username,
            timeoutSeconds = config.Database.TimeoutSeconds,
            theme = config.Ui.Theme
        });
    }

    private async Task<IpcResponse> HandleExportExcelAsync(string requestId, JsonElement? payload, CancellationToken cancellationToken)
    {
        if (_cachedSnapshot == null)
        {
            _cachedSnapshot = await _clinicalService.LoadClinicalSnapshotAsync(cancellationToken).ConfigureAwait(false);
        }

        ExportExcelPayload? payloadObj = null;
        if (payload.HasValue)
        {
            payloadObj = JsonSerializer.Deserialize<ExportExcelPayload>(payload.Value.GetRawText(), JsonOptions);
        }

        bool exportFilteredOnly = payloadObj?.ExportFilteredOnly == true;

        IReadOnlyList<MarScheduleItemDto> marToExport;
        IReadOnlyList<ActivePrescriptionDto> rxToExport;

        if (exportFilteredOnly && payloadObj?.FilteredScheduleIds != null)
        {
            var idSet = new HashSet<long>(payloadObj.FilteredScheduleIds);
            var filteredMar = _cachedSnapshot.MarSchedules.Where(s => idSet.Contains(s.ScheduleId)).ToList();
            var matchedOrderIds = filteredMar.Select(s => s.Med_Order_Id).ToHashSet();
            var filteredRx = _cachedSnapshot.ActivePrescriptions.Where(rx => matchedOrderIds.Contains(rx.Med_Order_Id)).ToList();

            marToExport = filteredMar;
            rxToExport = filteredRx;
        }
        else
        {
            marToExport = _cachedSnapshot.MarSchedules;
            rxToExport = _cachedSnapshot.ActivePrescriptions;
        }

        byte[] fileBytes = _excelService.GenerateMultiSheetExcel(marToExport, rxToExport);
        string filename = _excelService.GenerateTimestampedFilename();

        // Determine destination folder (Downloads if available, else BaseDirectory)
        string destinationDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads");

        if (!Directory.Exists(destinationDir))
        {
            destinationDir = AppContext.BaseDirectory;
        }

        string fullPath = Path.Combine(destinationDir, filename);
        await File.WriteAllBytesAsync(fullPath, fileBytes, cancellationToken).ConfigureAwait(false);

        return IpcResponse.Ok(requestId, new
        {
            filename = filename,
            filePath = fullPath,
            recordCount = marToExport.Count,
            fileSize = fileBytes.Length
        });
    }
}
