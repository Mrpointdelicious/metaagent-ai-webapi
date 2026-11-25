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
    /// <summary>Total re-derived from the recorded option scores; only present when the scale rule is verified.</summary>
    public decimal? RecomputedScore { get; init; }
    public decimal? ScaleMaxScore { get; init; }
    public string ScoringStatus { get; init; } = "unverified";
    public int QuestionCount { get; init; }
    /// <summary>Null when the source report did not record per-item selections; 0 would wrongly read as "answered nothing".</summary>
    public int? AnsweredItemCount { get; init; }
    public bool HasItemAnswers { get; init; }
    /// <summary>Per-item facts; only populated for single-record analysis, never for history or overview.</summary>
    public IReadOnlyList<AssessmentItemDto> Items { get; init; } = Array.Empty<AssessmentItemDto>();
    public string ScoreMeaning { get; init; } = "source_reported_total_only";
    public string? SourceScoreField { get; init; }
    public string SchemaFamily { get; init; } = "unrecognized";
    public IReadOnlyList<string> ReasonCodes { get; init; } = Array.Empty<string>();
    /// <summary>Patient-safe Chinese explanation for each reason code, in the same order.</summary>
    public IReadOnlyList<string> ReasonNotes { get; init; } = Array.Empty<string>();
    /// <summary>Reading produced by the per-scale rule registry; null when no traceable rule exists.</summary>
    public AssessmentInterpretationDto? Interpretation { get; init; }
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class AssessmentInterpretationDto
{
    /// <summary>descriptive = a band was applied; unverified = no verdict was produced.</summary>
    public string Level { get; init; } = "unverified";
    public string BandLabel { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    /// <summary>Why the reading says what it says; quoteable by the agent.</summary>
    public IReadOnlyList<string> Basis { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Cautions { get; init; } = Array.Empty<string>();
    public string EvidenceStatus { get; init; } = "unverified";
    public string EvidenceSource { get; init; } = string.Empty;
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class AssessmentStandardDto
{
    public int ScaleType { get; init; }
    public decimal MaxScore { get; init; }
    public string EvidenceStatus { get; init; } = "unverified";
    public string EvidenceSource { get; init; } = string.Empty;
    public bool IsBlocked { get; init; }
    public IReadOnlyList<AssessmentBandDto> Bands { get; init; } = Array.Empty<AssessmentBandDto>();
    public IReadOnlyList<string> Basis { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Cautions { get; init; } = Array.Empty<string>();
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class AssessmentBandDto
{
    public decimal Min { get; init; }
    public decimal Max { get; init; }
    public string Label { get; init; } = string.Empty;
    public string Text { get; init; } = string.Empty;
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class AssessmentItemDto
{
    /// <summary>1-based position of the item inside the instrument.</summary>
    public int Index { get; init; }
    /// <summary>0-based option slot as stored by the product; null when unanswered or not recorded.</summary>
    public int? OptionIndex { get; init; }
    public decimal? Score { get; init; }
    public decimal? MaxScore { get; init; }
    public bool Answered { get; init; }
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
