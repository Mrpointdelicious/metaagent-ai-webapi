using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace AI_WebApi.Model.DTOs.AITools;

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class GenerateIReTourSingleSessionReportRequestDto
{
    public string? ExpectedSourceSnapshot { get; init; }
    public DifySystemContextDto? SystemContext { get; init; }
    public string SessionRef { get; init; } = string.Empty;

    [JsonProperty("ai_evaluation")]
    public object? ForbiddenAiEvaluation { get; init; }

    [JsonProperty("report_type")]
    public object? ForbiddenReportType { get; init; }
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class IReTourSingleSessionReportArtifactDto
{
    public string SourceSnapshot { get; init; } = string.Empty;
    public string ArtifactRef { get; init; } = string.Empty;
    public string StorageMode { get; init; } = "ephemeral";
    public string MimeType { get; init; } = "image/png";
    public string ImageUrl { get; init; } = string.Empty;
    public string DisplayMarkdown { get; init; } = string.Empty;
    public DateTimeOffset? ExpiresAt { get; init; }
    public string TemplateVersion { get; init; } = "iretour-single-v3";
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class GenerateIReTourLongitudinalReportRequestDto
{
    public string? ExpectedSourceSnapshot { get; init; }
    public DifySystemContextDto? SystemContext { get; init; }
    public string SelectionMode { get; init; } = "latest_count";
    public int ReportCount { get; init; } = 4;
    public int? StartOrdinal { get; init; }
    public int? EndOrdinal { get; init; }
    public DateTimeOffset? StartDate { get; init; }
    public DateTimeOffset? EndDate { get; init; }
    public string ActivityScope { get; init; } = "straight_primary";

    [JsonProperty("report_type")]
    public object? ForbiddenReportType { get; init; }
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class IReTourLongitudinalReportArtifactDto
{
    public string SourceSnapshot { get; init; } = string.Empty;
    public string ArtifactRef { get; init; } = string.Empty;
    public string StorageMode { get; init; } = "ephemeral";
    public string MimeType { get; init; } = "image/png";
    public string ImageUrl { get; init; } = string.Empty;
    public string DisplayMarkdown { get; init; } = string.Empty;
    public DateTimeOffset? ExpiresAt { get; init; }
    public string TemplateVersion { get; init; } = "iretour-longitudinal-v3";
}
