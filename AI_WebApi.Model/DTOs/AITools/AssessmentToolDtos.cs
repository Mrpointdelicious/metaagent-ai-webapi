using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace AI_WebApi.Model.DTOs.AITools;

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class AssessmentToolRequestDto
{
    public DifySystemContextDto? SystemContext { get; init; }
    public string? DataScope { get; init; }
    public int PageNumber { get; init; } = 1;
    public int PageSize { get; init; } = 10;
    public int? ScaleType { get; init; }
    public string Selector { get; init; } = "latest_record";
    public string? AssessmentRef { get; init; }
    public string SelectionMode { get; init; } = "latest_count";
    public int ReportCount { get; init; } = 4;
    public DateTimeOffset? StartDate { get; init; }
    public DateTimeOffset? EndDate { get; init; }
    public string? ExpectedSourceSnapshot { get; init; }
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class AssessmentRecordDto
{
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public RecordTenantDto? RecordTenant { get; set; }
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public string? OwnerUserRef { get; set; }
    public string AssessmentRef { get; init; } = string.Empty;
    public int? ScaleType { get; init; }
    public string? ScaleName { get; init; }
    public string IdentityStatus { get; init; } = "unverified";
    public string InstrumentVersionStatus { get; init; } = "unverified";
    public string AssessmentOrigin { get; init; } = "unknown";
    public DateTimeOffset? RecordedAt { get; init; }
    public string TimeMeaning { get; init; } = "unknown";
    public string ResultState { get; init; } = "no_result";
    public decimal? ReportedScore { get; init; }
    public string ScoreMeaning { get; init; } = "source_reported_total_only";
    public string? SourceScoreField { get; init; }
    public string SchemaFamily { get; init; } = "unrecognized";
    public IReadOnlyList<string> ReasonCodes { get; init; } = Array.Empty<string>();
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class AssessmentToolDataDto
{
    public string ObjectType { get; init; } = "online_assessment";
    public string SourceSystem { get; init; } = "project";
    public string SourceSnapshot { get; set; } = string.Empty;
    public IReadOnlyDictionary<string, int> Counts { get; init; } = new Dictionary<string, int>();
    public IReadOnlyList<AssessmentScaleCoverageDto> ScaleCoverage { get; init; } = Array.Empty<AssessmentScaleCoverageDto>();
    public IReadOnlyList<AssessmentRecordDto> Records { get; init; } = Array.Empty<AssessmentRecordDto>();
    public int PageNumber { get; init; }
    public bool HasNext { get; init; }
    public string Comparability { get; init; } = "not_requested";
    public IReTourReadingDto Interpretation { get; init; } = new();
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class AssessmentScaleCoverageDto
{
    public int ScaleType { get; init; }
    public string ScaleName { get; init; } = string.Empty;
    public string ScoringStatus { get; init; } = "unverified";
    public int RecordCount { get; init; }
    public int ReadableTotalCount { get; init; }
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class AssessmentReportArtifactDto
{
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public IReadOnlyList<PatientTenantSegmentDto>? TenantSegments { get; init; }
    public string ArtifactRef { get; init; } = string.Empty;
    public string ImageUrl { get; init; } = string.Empty;
    public string MimeType { get; init; } = "image/png";
    public string DisplayMarkdown { get; init; } = string.Empty;
    public string SourceSnapshot { get; init; } = string.Empty;
    public string TemplateVersion { get; init; } = string.Empty;
    public string StorageMode { get; init; } = string.Empty;
    public DateTimeOffset? ExpiresAt { get; init; }
}
