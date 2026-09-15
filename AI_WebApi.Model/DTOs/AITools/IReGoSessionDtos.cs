using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace AI_WebApi.Model.DTOs.AITools;

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class GetIReGoSessionAnalysisRequestDto
{
    public DifySystemContextDto? SystemContext { get; init; }
    public string Selector { get; init; } = "latest_usable";
    public string? SessionRef { get; init; }
    public string DetailLevel { get; init; } = "standard";
    public string QualityDetail { get; init; } = "summary";
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class IReGoSessionAnalysisDto
{
    public IReGoSessionIdentityDto Session { get; init; } = new();
    public IReGoSessionTimeDto Time { get; init; } = new();
    public IReGoSessionSchemaDto Schema { get; init; } = new();
    public IReGoSessionOverviewDto Overview { get; init; } = new();
    public IReGoPlanDetailDto Plan { get; init; } = new();
    public IReadOnlyList<IReGoContentBlockDto> ReportBlocks { get; init; } =
        Array.Empty<IReGoContentBlockDto>();
    public IReadOnlyList<IReGoBlockComparisonDto> BlockComparison { get; init; } =
        Array.Empty<IReGoBlockComparisonDto>();
    public IReGoSingleSessionEvaluationFactsDto ReportEvaluationFacts
        { get; init; } = new();
    public IReadOnlyList<IReGoCurrentPerformanceFacetDto>
        CurrentPerformanceFacets { get; init; } =
        Array.Empty<IReGoCurrentPerformanceFacetDto>();

    /// <summary>
    /// Compatibility projections retained for existing 1.1 clients.
    /// New clients should use report_blocks.
    /// </summary>
    public IReadOnlyList<IReGoContentBlockDto> DirectTraining { get; init; } =
        Array.Empty<IReGoContentBlockDto>();
    public IReadOnlyList<IReGoContentBlockDto> Games { get; init; } =
        Array.Empty<IReGoContentBlockDto>();
    public IReadOnlyList<IReGoContentBlockDto> Assessments { get; init; } =
        Array.Empty<IReGoContentBlockDto>();
    public PlanReportComparisonDto PlanReportComparison { get; init; } = new();
    public IReGoMappingDto Mapping { get; init; } = new();
    public QualitySummaryDto Quality { get; init; } = new();
    public IReadOnlyList<IReGoSourceDto> Sources { get; init; } =
        Array.Empty<IReGoSourceDto>();
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class IReGoSingleSessionEvaluationFactsDto
{
    public decimal? CompletionRate { get; init; }
    public string CompletionLevel { get; init; } = "unavailable";
    public string CompletionBasis { get; init; } =
        "planned_report_block_match";
    public int CompletionNumerator { get; init; }
    public int CompletionDenominator { get; init; }
    public IReadOnlyList<IReGoGameEvaluationFactDto> Games { get; init; } =
        Array.Empty<IReGoGameEvaluationFactDto>();
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class IReGoGameEvaluationFactDto
{
    public int ReportSequence { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public decimal? Score { get; init; }
    public IReadOnlyList<string> AllowedPerformanceLevels { get; init; } =
        new[] { "general", "good", "excellent" };
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class IReGoSessionIdentityDto
{
    public string SessionRef { get; init; } = string.Empty;
    public string PatientRef { get; init; } = string.Empty;
    public string DeviceFamily { get; init; } = "irego";
    public string SourceSystem { get; init; } = "project";
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class IReGoSessionTimeDto
{
    public DateTimeOffset? ScheduledAt { get; init; }
    public DateTimeOffset? SourceRecordCreatedAt { get; init; }
    public DateTimeOffset? ExecutionStartedAt { get; init; }
    public DateTimeOffset? ReportRecordedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    public string TrainingState { get; init; } = "execution_unknown";
    public string StateConfidence { get; init; } = "low";
    public string TimeZone { get; init; } = "Asia/Shanghai";
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class IReGoSessionSchemaDto
{
    public string PlanSchemaFamily { get; init; } = "unknown";
    public string ReportSchemaFamily { get; init; } = "unknown";
    public string ReportSchemaSignature { get; init; } = string.Empty;
    public string ValidationStatus { get; init; } = "invalid";
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class IReGoSessionOverviewDto
{
    /// <summary>
    /// Compatibility alias for ReportDetails.ReportList element count.
    /// Each element is one report record block, not an independent rehab project.
    /// </summary>
    public int ReportItemCount { get; init; }
    public int ReportBlockCount { get; init; }
    public string ReportBlockLabel { get; init; } = "报告记录分段数";
    public IReadOnlyList<string> ContentTypes { get; init; } = Array.Empty<string>();
    public int ValidMetricCount { get; init; }
    public int ExcludedMetricCount { get; init; }
    public decimal? TotalTrainingDuration { get; init; }
    public string? DurationUnit { get; init; }
    public string? TotalTrainingDurationDisplay { get; init; }
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class IReGoContentBlockDto
{
    public int Sequence { get; init; }
    public int? ReportMode { get; init; }
    public int? BalanceMode { get; init; }
    public string RehabContent { get; init; } = "unknown";
    public string InteractionType { get; init; } = "direct_training";
    public string? Name { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public string? TrainingModeName { get; init; }
    public IReadOnlyList<MetricFactDto> Metrics { get; init; } =
        Array.Empty<MetricFactDto>();
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class MetricFactDto
{
    public string MetricCode { get; init; } = string.Empty;
    public object? RawValue { get; init; }
    public decimal? NormalizedValue { get; init; }
    public string ValueStatus { get; init; } = "valid";
    public string? Unit { get; init; }
    public string UnitStatus { get; init; } = "unknown";
    public string DataStage { get; init; } = "report_derived";
    public string SourceField { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string SourceKind { get; init; } = "raw";
    public string MetricEvidenceLevel { get; init; } = "source_fact";
    public string EvaluationStatus { get; init; } = "eligible_for_review";
    public IReGoMetricRepairEvidenceDto? Repair { get; init; }
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class IReGoMetricRepairEvidenceDto
{
    public string Rule { get; init; } = string.Empty;
    public string RuleVersion { get; init; } = string.Empty;
    public IReadOnlyList<string> SourceFields { get; init; } =
        Array.Empty<string>();
    public string Confidence { get; init; } = "unknown";
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class IReGoCurrentPerformanceFacetDto
{
    public string CapabilityCode { get; init; } = string.Empty;
    public string CapabilityName { get; init; } = string.Empty;
    public IReadOnlyList<string> MetricCodes { get; init; } =
        Array.Empty<string>();
    public string InterpretationMode { get; init; } = "current_fact_only";
    public string PatientMessage { get; init; } = string.Empty;
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class IReGoPlanDetailDto
{
    public string PlanRef { get; init; } = string.Empty;
    public string State { get; init; } = "execution_unknown";
    public string CompletionFlag { get; init; } = "unknown";
    public string? StateReason { get; init; }
    public string? PatientMessage { get; init; }
    public string Summary { get; init; } = string.Empty;
    public IReadOnlyList<IReGoContentBlockDto> Blocks { get; init; } =
        Array.Empty<IReGoContentBlockDto>();
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class IReGoBlockComparisonDto
{
    public int? PlanSequence { get; init; }
    public int? ReportSequence { get; init; }
    public string? PlanDisplayName { get; init; }
    public string? ReportDisplayName { get; init; }
    public string Status { get; init; } = "ambiguous_match";
    public IReadOnlyList<string> MatchedSignals { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> MissingFromReport { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> UnplannedInReport { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    public IReadOnlyList<IReGoMetricComparisonDto> MetricComparisons { get; init; } =
        Array.Empty<IReGoMetricComparisonDto>();
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class IReGoMetricComparisonDto
{
    public string PlannedMetricCode { get; init; } = string.Empty;
    public string? ReportedMetricCode { get; init; }
    public decimal? PlannedValue { get; init; }
    public decimal? ReportedValue { get; init; }
    public string? Unit { get; init; }
    public bool Comparable { get; init; }
    public string Relation { get; init; } = "not_comparable";
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class PlanReportComparisonDto
{
    public string Status { get; init; } = "not_comparable";
    public IReadOnlyList<string> PlannedContent { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> ReportedContent { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> MissingFromReport { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> UnplannedInReport { get; init; } = Array.Empty<string>();
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class IReGoMappingDto
{
    public string DeviceRule { get; init; } =
        "project.rehabplan.devicetype=1 AND project.templates.modelid=1";
    public string PlanReportRule { get; init; } =
        "project.report.Id=project.rehabplan.Id";
    public string PrimaryReportContainer { get; init; } = "ReportDetails.ReportList";
    public string ProcessFieldRole { get; init; } = "sequence_consistency_only";
    public string RevisionPolicy { get; init; } = "single_summary_report_per_plan";
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class QualitySummaryDto
{
    public string OverallStatus { get; init; } = "unknown";
    public string TrendEligibility { get; init; } = "ineligible";
    public string LinkStatus { get; init; } = "unknown";
    public string? PatientMessage { get; init; }
    public string? ExplanationBoundary { get; init; }
    public IReadOnlyList<ToolIssueDto> Issues { get; init; } =
        Array.Empty<ToolIssueDto>();
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class IReGoSourceDto
{
    public string SourceSystem { get; init; } = string.Empty;
    public string SourceObject { get; init; } = string.Empty;
    public string Role { get; init; } = string.Empty;
    public string Completeness { get; init; } = "partial";
}
