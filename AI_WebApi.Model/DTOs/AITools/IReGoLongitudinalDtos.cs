using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace AI_WebApi.Model.DTOs.AITools;

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class GetIReGoLongitudinalAnalysisRequestDto
{
    public DifySystemContextDto? SystemContext { get; init; }
    public string? DataScope { get; init; }

    /// <summary>
    /// Semantic selection that never exposes database or signed session ids.
    /// latest_count, recent_ordinal_range, or date_range.
    /// </summary>
    public string SelectionMode { get; init; } = "latest_count";
    public int? StartOrdinal { get; init; }
    public int? EndOrdinal { get; init; }
    public DateTimeOffset? StartDate { get; init; }
    public DateTimeOffset? EndDate { get; init; }

    /// <summary>
    /// latest_contiguous: latest ReportCount report positions;
    /// session_refs: the earliest and latest supplied references delimit the window.
    /// Any report position between them is included automatically.
    /// </summary>
    public string Selector { get; init; } = "latest_contiguous";

    public int ReportCount { get; init; } = 4;

    /// <summary>
    /// Stable references previously returned by an IREGO tool. The model may
    /// select them but must never invent them.
    /// </summary>
    public IReadOnlyList<string> SessionRefs { get; init; } =
        Array.Empty<string>();

    [JsonProperty("session_refs_json")]
    public object? ForbiddenSessionRefsJson { get; init; }

    public string ProjectScope { get; init; } = "all";
    public string DetailLevel { get; init; } = "standard";
    public string QualityDetail { get; init; } = "summary";
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class IReGoLongitudinalAnalysisDto
{
    public string SourceSnapshot { get; set; } = string.Empty;
    public string PatientRef { get; init; } = string.Empty;
    public IReGoLongitudinalWindowDto Window { get; init; } = new();
    public IReadOnlyList<IReGoLongitudinalReportPositionDto> Reports
        { get; init; } = Array.Empty<IReGoLongitudinalReportPositionDto>();
    public IReadOnlyList<IReGoLongitudinalProjectDto> Projects
        { get; init; } = Array.Empty<IReGoLongitudinalProjectDto>();
    /// <summary>
    /// Patient-readable, aspect-level evaluations. These verdicts describe
    /// movement performance only and are never combined into a disease score.
    /// </summary>
    public IReadOnlyList<IReGoCapabilityEvaluationDto> CapabilityEvaluations
        { get; init; } = Array.Empty<IReGoCapabilityEvaluationDto>();
    public IReadOnlyList<string> AnswerableQuestions { get; init; } =
        Array.Empty<string>();
    public QualitySummaryDto Quality { get; init; } = new();
    public IReadOnlyList<IReGoSourceDto> Sources { get; init; } =
        Array.Empty<IReGoSourceDto>();
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class IReGoLongitudinalWindowDto
{
    public string Selector { get; init; } = string.Empty;
    public int RequestedReportCount { get; init; }
    public int ResolvedReportCount { get; init; }
    public int CompleteReportCount { get; init; }
    public int IncompleteReportCount { get; init; }
    public bool IsContiguous { get; init; }
    public DateTimeOffset? StartedAt { get; init; }
    public DateTimeOffset? EndedAt { get; init; }
    public string ComparisonMode { get; init; } = "unavailable";
    public string MissingPointPolicy { get; init; } =
        "keep_report_time_position_connect_existing_points_solid";
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class IReGoLongitudinalReportPositionDto
{
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public string? RecordRef { get; init; }
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public RecordTenantDto? RecordTenant { get; set; }
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public string? OwnerUserRef { get; set; }
    public int Ordinal { get; init; }
    public string SessionRef { get; init; } = string.Empty;
    public DateTimeOffset? SessionTime { get; init; }
    public string TrainingState { get; init; } = "execution_unknown";
    public string ReportStatus { get; init; } = "incomplete";
    public bool IncludedInMetricComparison { get; init; }
    public int ReportBlockCount { get; init; }
    public decimal? CompletionRate { get; init; }
    public int CompletionNumerator { get; init; }
    public int CompletionDenominator { get; init; }
    public string CompletionLevel { get; init; } = "unavailable";
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class IReGoLongitudinalProjectDto
{
    public string ProjectKey { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string RehabContent { get; init; } = "unknown";
    public string InteractionType { get; init; } = "direct_training";
    public int? GameMode { get; init; }
    public int? BalanceMode { get; init; }
    public int SameProjectLaneCount { get; init; }
    public IReadOnlyList<IReGoLongitudinalLaneDto> Lanes { get; init; } =
        Array.Empty<IReGoLongitudinalLaneDto>();
    public IReadOnlyList<IReGoCapabilityEvaluationDto> CapabilityEvaluations
        { get; init; } = Array.Empty<IReGoCapabilityEvaluationDto>();
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class IReGoLongitudinalLaneDto
{
    public int SameProjectOrdinal { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public IReadOnlyList<IReGoLongitudinalSegmentDto> Segments { get; init; } =
        Array.Empty<IReGoLongitudinalSegmentDto>();
    public IReadOnlyList<IReGoLongitudinalMetricSeriesDto> MetricSeries
        { get; init; } = Array.Empty<IReGoLongitudinalMetricSeriesDto>();
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class IReGoLongitudinalSegmentDto
{
    public int ReportOrdinal { get; init; }
    public string Status { get; init; } = "segment_missing";
    public int? ReportSequence { get; init; }
    public string? DisplayName { get; init; }
    public string? ConfigurationSignature { get; init; }
    public int? GameDifficulty { get; init; }
    public IReadOnlyList<MetricFactDto> Metrics { get; init; } =
        Array.Empty<MetricFactDto>();
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class IReGoLongitudinalMetricSeriesDto
{
    public string MetricCode { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string SchemaFamily { get; init; } = "unknown";
    public string? Unit { get; init; }
    public string UnitStatus { get; init; } = "unknown";
    public bool ComparisonEligible { get; init; }
    public bool ChartEligible { get; init; }
    public int LongestSameConditionRun { get; init; }
    public IReadOnlyList<string> ComparisonLimitations { get; init; } =
        Array.Empty<string>();
    public IReadOnlyList<IReGoLongitudinalMetricPointDto> Points { get; init; } =
        Array.Empty<IReGoLongitudinalMetricPointDto>();
    public IReGoLongitudinalInterpretationDto Interpretation { get; init; } =
        new();
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class IReGoLongitudinalMetricPointDto
{
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public RecordTenantDto? RecordTenant { get; set; }
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public string? OwnerUserRef { get; set; }
    public int ReportOrdinal { get; init; }
    public string SessionRef { get; init; } = string.Empty;
    public string Status { get; init; } = "metric_missing";
    public object? RawValue { get; init; }
    public decimal? NormalizedValue { get; init; }
    public string? DisplayValue { get; init; }
    public int? GameDifficulty { get; init; }
    public string? ConfigurationSignature { get; init; }
    public string LineFromPrevious { get; init; } = "none";
    public string ComparisonScope { get; init; } = "none";
    public IReadOnlyList<string> ComparisonLimitations { get; init; } =
        Array.Empty<string>();
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class IReGoLongitudinalInterpretationDto
{
    public string MetricCode { get; init; } = string.Empty;
    public string Mode { get; init; } = "unavailable";
    public string Direction { get; init; } = "insufficient";
    public string Text { get; init; } = string.Empty;
    public string Verdict { get; init; } = "insufficient_data";
    public string CapabilityCode { get; init; } = string.Empty;
    public string CapabilityName { get; init; } = string.Empty;
    public string Strength { get; init; } = "unavailable";
    public decimal? Delta { get; init; }
    public IReGoMetricThresholdDto? Threshold { get; init; }
    /// <summary>Voice-ready conclusion. Mirrors PatientMessage.</summary>
    public string BasicEvaluation { get; init; } = string.Empty;
    /// <summary>Patient-readable facts and rule basis for a follow-up question.</summary>
    public string ExtendedExplanation { get; init; } = string.Empty;
    /// <summary>
    /// Guarded relevance to daily activity. It never asserts disease recovery
    /// or independence from a training metric alone.
    /// </summary>
    public string LifeHint { get; init; } = string.Empty;
    public string PatientMessage { get; init; } = string.Empty;
    public string MessageRuleVersion { get; init; } = string.Empty;
    public string RuleSetVersion { get; init; } = string.Empty;
    public string ExplanationBoundary { get; init; } =
        "AI 依据训练表现生成评价，不等同于病情改善。";
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class IReGoMetricThresholdDto
{
    public decimal Value { get; init; }
    public string Unit { get; init; } = string.Empty;
    public string Source { get; init; } = "unavailable";
    public string EvidenceLevel { get; init; } = "none";
    public bool IsIReGoMdc { get; init; }
    public bool ProductionUsable { get; init; }
    public decimal? DatabaseExploratoryValue { get; init; }
    public decimal? ExternalLowerBound { get; init; }
    public decimal? ObservableChangeFloor { get; init; }
    public decimal? RelativeDifferenceFromExternal { get; init; }
    public string SelectionPolicy { get; init; } = string.Empty;
    public string? EvidenceReference { get; init; }
    public string RegistryVersion { get; init; } = string.Empty;
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class IReGoCapabilityEvaluationDto
{
    public string CapabilityCode { get; init; } = string.Empty;
    public string CapabilityName { get; init; } = string.Empty;
    public string Verdict { get; init; } = "insufficient_data";
    public string EvaluationAxis { get; init; } = "result_performance";
    public string Strength { get; init; } = "unavailable";
    /// <summary>Voice-ready conclusion. Mirrors PatientMessage.</summary>
    public string BasicEvaluation { get; init; } = string.Empty;
    /// <summary>Patient-readable evidence and rule explanation.</summary>
    public string ExtendedExplanation { get; init; } = string.Empty;
    /// <summary>Guarded activity relevance for stroke rehabilitation.</summary>
    public string LifeHint { get; init; } = string.Empty;
    [JsonIgnore]
    public string PatientMessage { get; init; } = string.Empty;
    public IReadOnlyList<string> EvidenceMetricCodes { get; init; } =
        Array.Empty<string>();
    public string MessageRuleVersion { get; init; } = string.Empty;
    public string RuleSetVersion { get; init; } = string.Empty;
    public string ExplanationBoundary { get; init; } =
        "AI 依据训练表现生成评价，不等同于病情改善。";
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class GenerateIReGoLongitudinalReportRequestDto
{
    public string? ExpectedSourceSnapshot { get; init; }
    public DifySystemContextDto? SystemContext { get; init; }
    public string? DataScope { get; init; }
    public string SelectionMode { get; init; } = "latest_count";
    public int? StartOrdinal { get; init; }
    public int? EndOrdinal { get; init; }
    public DateTimeOffset? StartDate { get; init; }
    public DateTimeOffset? EndDate { get; init; }
    public string Selector { get; init; } = "latest_contiguous";
    public int ReportCount { get; init; } = 4;
    public IReadOnlyList<string> SessionRefs { get; init; } =
        Array.Empty<string>();
    [JsonProperty("session_refs_json")]
    public object? ForbiddenSessionRefsJson { get; init; }
    [JsonProperty("report_type")]
    public object? ForbiddenReportType { get; init; }
    public string ProjectScope { get; init; } = "all";
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class IReGoLongitudinalReportArtifactDto
{
    public string SourceSnapshot { get; init; } = string.Empty;
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public IReadOnlyList<PatientTenantSegmentDto>? TenantSegments { get; init; }
    public string ArtifactRef { get; init; } = string.Empty;
    public string StorageMode { get; init; } = "ephemeral";
    public string MimeType { get; init; } = "image/png";
    public string ImageUrl { get; init; } = string.Empty;
    public string DisplayMarkdown { get; init; } = string.Empty;
    public DateTimeOffset? ExpiresAt { get; init; }
    public string TemplateVersion { get; init; } = "irego-longitudinal-v6";
}
