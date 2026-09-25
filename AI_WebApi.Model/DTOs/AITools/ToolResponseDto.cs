using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace AI_WebApi.Model.DTOs.AITools;

/// <summary>
/// Stable envelope shared by model-facing patient tools.
/// Business-level unavailability is represented by status=unavailable with HTTP 200.
/// </summary>
[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class ToolResponseDto<T>
{
    public string ToolName { get; init; } = string.Empty;
    public string ContractVersion { get; init; } = IReGoContract.Version;
    public string RequestId { get; init; } = string.Empty;
    public string Status { get; init; } = ToolStatuses.Success;
    public T? Data { get; init; }
    public IReadOnlyList<string> ReasonCodes { get; init; } = Array.Empty<string>();
    public string? PatientMessage { get; init; }
    public ToolMetaDto Meta { get; init; } = new();

    public static ToolResponseDto<T> Create(
        string toolName,
        string requestId,
        string status,
        T? data,
        IEnumerable<string>? reasonCodes,
        ToolMetaDto meta,
        string? patientMessage = null,
        string? contractVersion = null) =>
        new()
        {
            ToolName = toolName,
            ContractVersion = contractVersion ?? IReGoContract.Version,
            RequestId = requestId,
            Status = status,
            Data = data,
            ReasonCodes = reasonCodes?.Distinct().ToArray() ?? Array.Empty<string>(),
            PatientMessage = patientMessage,
            Meta = meta
        };
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class ToolMetaDto
{
    public DateTimeOffset GeneratedAt { get; init; } = DateTimeOffset.UtcNow;
    public string SourceCompleteness { get; init; } = "unknown";
    public string? Watermark { get; init; }
    public RegistryVersionsDto RegistryVersions { get; init; } = new();
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class RegistryVersionsDto
{
    public string Contract { get; init; } = IReGoContract.Version;
    public string Device { get; init; } = IReGoContract.DeviceRegistryVersion;
    public string Schema { get; init; } = IReGoContract.SchemaRegistryVersion;
    public string Metric { get; init; } = IReGoContract.MetricRegistryVersion;
    public string State { get; init; } = IReGoContract.StateRegistryVersion;
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class ToolIssueDto
{
    public string Code { get; init; } = string.Empty;
    public string Severity { get; init; } = "warning";
    public string Scope { get; init; } = "session";
    public string Action { get; init; } = "none";
    public string? Detail { get; init; }
    public string? PatientMessage { get; init; }
}

public static class ToolStatuses
{
    public const string Available = "available";
    public const string Success = "success";
    public const string Partial = "partial";
    public const string Unavailable = "unavailable";
    public const string Failed = "failed";
}

public static class IReGoContract
{
    public const string Version = "1.6.0";
    public const string DeviceRegistryVersion = "robotdb-main-device-1.0.0";
    public const string SchemaRegistryVersion = "robotdb-main-schema-1.0.0";
    public const string MetricRegistryVersion = "robotdb-main-metric-2.0.0";
    public const string StateRegistryVersion = "robotdb-main-state-1.0.0";
}
