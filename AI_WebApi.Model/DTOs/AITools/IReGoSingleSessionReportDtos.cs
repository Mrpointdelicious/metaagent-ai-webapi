using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace AI_WebApi.Model.DTOs.AITools;

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class GenerateIReGoSingleSessionReportRequestDto
{
    public DifySystemContextDto? SystemContext { get; init; }
    public string SessionRef { get; init; } = string.Empty;

    [JsonProperty("ai_evaluation")]
    public object? ForbiddenAiEvaluation { get; init; }

    [JsonProperty("report_type")]
    public object? ForbiddenReportType { get; init; }
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class IReGoSingleSessionAiEvaluationDto
{
    /// <summary>
    /// AI-selected presentation label. The backend recomputes the completion
    /// rate and rejects a label that does not match low/boundary/high.
    /// </summary>
    public string CompletionLevel { get; init; } = string.Empty;

    /// <summary>
    /// One entry for every report game block that has a valid game score.
    /// ReportSequence is the ReportList order, never a database primary key.
    /// </summary>
    public IReadOnlyList<IReGoGamePerformanceEvaluationDto> GameEvaluations
        { get; init; } = Array.Empty<IReGoGamePerformanceEvaluationDto>();
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class IReGoGamePerformanceEvaluationDto
{
    public int ReportSequence { get; init; }
    public string PerformanceLevel { get; init; } = string.Empty;
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class IReGoSingleSessionReportArtifactDto
{
    public string ArtifactRef { get; init; } = string.Empty;
    public string StorageMode { get; init; } = "ephemeral";
    public string MimeType { get; init; } = "image/png";
    public string ImageUrl { get; init; } = string.Empty;
    public string DisplayMarkdown { get; init; } = string.Empty;
    public DateTimeOffset? ExpiresAt { get; init; }
    public string TemplateVersion { get; init; } = "irego-single-v2";
}

public enum ReportImageAccessStatus
{
    Available,
    Invalid,
    Expired,
    Missing
}

public sealed class ReportImageAccessResult
{
    public ReportImageAccessStatus Status { get; init; }
    public string ArtifactRef { get; init; } = string.Empty;
    public byte[]? Bytes { get; init; }
    public string MimeType { get; init; } = "image/png";
}
