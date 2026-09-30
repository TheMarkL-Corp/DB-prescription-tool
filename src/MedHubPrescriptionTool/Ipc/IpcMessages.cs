using System.Text.Json;

namespace MedHubPrescriptionTool.Ipc;

/// <summary>
/// Incoming message model from the web frontend via window.external.sendMessage.
/// </summary>
public class IpcRequest
{
    public string RequestId { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public JsonElement? Payload { get; set; }
}

/// <summary>
/// Outgoing response model returned to the web frontend via window.SendWebMessage.
/// </summary>
public class IpcResponse
{
    public string RequestId { get; set; } = string.Empty;
    public string? Action { get; set; }
    public bool Success { get; set; }
    public object? Data { get; set; }
    public string? Error { get; set; }

    public static IpcResponse Ok(string requestId, object? data) =>
        new() { RequestId = requestId, Success = true, Data = data };

    public static IpcResponse Fail(string requestId, string error) =>
        new() { RequestId = requestId, Success = false, Error = error };
}

/// <summary>
/// Payload received when testing or saving database connection parameters.
/// </summary>
public class DbSettingsPayload
{
    public string? Host { get; set; }
    public int? Port { get; set; }
    public string? Database { get; set; }
    public string? Username { get; set; }
    public string? Password { get; set; }
}

/// <summary>
/// Payload received when requesting an Excel workbook export.
/// </summary>
public class ExportExcelPayload
{
    public bool ExportFilteredOnly { get; set; }
    public int? FilteredCount { get; set; }
    public List<long>? FilteredScheduleIds { get; set; }
}
