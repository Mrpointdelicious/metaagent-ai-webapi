using AI_WebApi.Model.AITools;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace AI_WebApi.Model.DTOs.AITools;

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class GetMultisourcePatientContextRequestDto
{
    public DifySystemContextDto? SystemContext { get; init; }
    public bool ForceRefresh { get; init; }
    public string ProjectionLevel { get; init; } = "compact";
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class DifySystemContextDto
{
    public string User { get; init; } = string.Empty;
    public string? ConversationId { get; init; }
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class MultisourcePatientContextDto
{
    public string PatientRef { get; init; } = string.Empty;
    public IdentityBindingDto IdentityBinding { get; init; } = new();
    public PatientProfileBriefDto ProfileBrief { get; init; } = new();
    public PatientDataDomainsDto DataDomains { get; init; } = new();
    public ContextSnapshotDto Snapshot { get; init; } = new();
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class IdentityBindingDto
{
    public string SubjectRef { get; init; } = string.Empty;
    public string PatientRef { get; init; } = string.Empty;
    public string TenantRef { get; init; } = string.Empty;
    public string Role { get; init; } = string.Empty;
    public string BindingStatus { get; init; } = "verified";
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class PatientProfileBriefDto
{
    public bool Available { get; init; }
    public IReadOnlyDictionary<string, object?> Facts { get; init; } =
        new Dictionary<string, object?>();
    public string Source { get; init; } = "project.dbuser.UserDetails";
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class PatientDataDomainsDto
{
    public IReGoContextDomainDto Irego { get; init; } = new();
    public DomainAvailabilityDto Movement { get; init; } = DomainAvailabilityDto.NotImplemented();
    public DomainAvailabilityDto Medication { get; init; } = DomainAvailabilityDto.NotImplemented();
    public DomainAvailabilityDto Assessment { get; init; } = DomainAvailabilityDto.NotImplemented();
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class DomainAvailabilityDto
{
    public string Availability { get; init; } = "not_implemented";
    public IReadOnlyList<string> ReasonCodes { get; init; } =
        new[] { IReGoReasonCodes.DomainNotImplemented };

    public static DomainAvailabilityDto NotImplemented() => new();
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class IReGoContextDomainDto
{
    public string Availability { get; init; } = "unavailable";
    public string DataState { get; init; } = "none";
    public IReGoAvailabilityDto Counts { get; init; } = new();
    public IReGoAvailabilityFlagsDto AvailabilityFlags { get; init; } = new();
    public IReGoCoverageDto Coverage { get; init; } = new();
    public IReGoPlanStatusSummaryDto PlanStatusSummary { get; init; } = new();
    public IReadOnlyList<IReGoPlanListItemDto> FuturePlans { get; init; } =
        Array.Empty<IReGoPlanListItemDto>();
    public IReadOnlyList<IReGoPlanListItemDto> UnfinishedPlans { get; init; } =
        Array.Empty<IReGoPlanListItemDto>();
    public IReadOnlyList<IReGoPlanListItemDto> VoidedPlans { get; init; } =
        Array.Empty<IReGoPlanListItemDto>();
    public IReadOnlyList<IReGoRecentSessionDto> RecentSessions { get; init; } =
        Array.Empty<IReGoRecentSessionDto>();
    public IReGoSessionHistoryDto SessionHistory { get; init; } = new();
    public LatestIReGoSessionDto? Latest { get; init; }
    public IReGoQueryCapabilitiesDto QueryCapabilities { get; init; } = new();
    public IReadOnlyList<IReGoQueryCapabilityDto> QueryCapabilityDetails { get; init; } =
        Array.Empty<IReGoQueryCapabilityDto>();
    public IReadOnlyList<IReGoEvaluationCapabilityDto> EvaluationCapabilities
        { get; init; } = Array.Empty<IReGoEvaluationCapabilityDto>();
    public IReadOnlyList<IReGoPausedMetricDto> PausedMetrics { get; init; } =
        Array.Empty<IReGoPausedMetricDto>();
    public IReGoThresholdCoverageDto ThresholdCoverage { get; init; } = new();
    public IReGoHistoryNavigationDto HistoryNavigation { get; init; } = new();
    public QualitySummaryDto Quality { get; init; } = new();
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class IReGoEvaluationCapabilityDto
{
    public string CapabilityCode { get; init; } = string.Empty;
    public string CapabilityName { get; init; } = string.Empty;
    public string SupportLevel { get; init; } = "current_fact";
    public string PatientMessage { get; init; } = string.Empty;
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class IReGoPausedMetricDto
{
    public string MetricCode { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string PatientMessage { get; init; } = string.Empty;
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class IReGoThresholdCoverageDto
{
    public int IReGoMdcCount { get; init; }
    public int DatabaseThresholdCount { get; init; }
    public int ExternalThresholdProfileCount { get; init; }
    public int PausedMetricCount { get; init; }
    public string RegistryVersion { get; init; } = string.Empty;
    public string SelectionPolicy { get; init; } = string.Empty;
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class IReGoHistoryNavigationDto
{
    public string ToolName { get; init; } = "get_irego_patient_history";
    public bool Pageable { get; init; } = true;
    public int MaximumPageSize { get; init; } = 50;
    public string PatientMessage { get; init; } =
        "可以按页继续查询既往训练、未完成计划和未来计划。";
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class IReGoAvailabilityDto
{
    public int StrictPlanCount { get; init; }
    public int ReportCount { get; init; }
    public int UsableSessionCount { get; init; }
    public int ReferenceAnomalyCount { get; init; }
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class IReGoAvailabilityFlagsDto
{
    public bool HasEligiblePlan { get; init; }
    public bool HasLinkedReport { get; init; }
    public bool HasUsableSession { get; init; }
    public bool HasFuturePlan { get; init; }
    public bool HasUnfinishedPlan { get; init; }
    public bool HasVoidedPlan { get; init; }
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class IReGoCoverageDto
{
    public int EligiblePlanCount { get; init; }
    public int EffectiveReportCount { get; init; }
    public int UsableSessionCount { get; init; }
    public int PlanOnlyCount { get; init; }
    public int FuturePlanCount { get; init; }
    public int UnfinishedPlanCount { get; init; }
    public int ExecutionUnknownCount { get; init; }
    public int VoidedPlanCount { get; init; }
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class IReGoPlanStatusSummaryDto
{
    public int Completed { get; init; }
    public int NotStarted { get; init; }
    public int NotCompleted { get; init; }
    public int Interrupted { get; init; }
    public int ExecutionUnknown { get; init; }
    public int Voided { get; init; }
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class IReGoPlanListItemDto
{
    public string PlanRef { get; init; } = string.Empty;
    public DateTimeOffset? ScheduledAt { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public string State { get; init; } = "execution_unknown";
    public string CompletionFlag { get; init; } = "unknown";
    public string? StateReason { get; init; }
    public string? PatientMessage { get; init; }
    public bool DetailAvailable { get; init; }
    public IReadOnlyList<IReGoContentBlockDto> PlannedBlocks { get; init; } =
        Array.Empty<IReGoContentBlockDto>();
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class IReGoRecentSessionDto
{
    public int Ordinal { get; init; }
    public string SessionRef { get; init; } = string.Empty;
    public DateTimeOffset? SessionTime { get; init; }
    public string TimeType { get; init; } = "unknown";
    public string TimeConfidence { get; init; } = "low";
    public string State { get; init; } = "execution_unknown";
    public IReadOnlyList<string> ContentTypes { get; init; } = Array.Empty<string>();
    public int ReportBlockCount { get; init; }
    public string DisplaySummary { get; init; } = string.Empty;
    public bool DetailAvailable { get; init; }
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class IReGoSessionHistoryDto
{
    public int TotalCount { get; init; }
    public int ReturnedCount { get; init; }
    public bool HasMore { get; init; }
    public string WindowPolicy { get; init; } = "latest_10_max_20";
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class LatestIReGoSessionDto
{
    public string SessionRef { get; init; } = string.Empty;
    public DateTimeOffset? SessionTime { get; init; }
    public string TrainingState { get; init; } = "execution_unknown";
    public IReadOnlyList<string> ContentTypes { get; init; } = Array.Empty<string>();
    public string QualityStatus { get; init; } = "unknown";
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class IReGoQueryCapabilitiesDto
{
    public bool LatestUsable { get; init; }
    public bool BySessionRef { get; init; }
    public bool FullAnalysis { get; init; }
    public bool Trend { get; init; }
    public IReadOnlyList<string> BlockedReasonCodes { get; init; } = Array.Empty<string>();
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class IReGoQueryCapabilityDto
{
    public string Capability { get; init; } = string.Empty;
    public bool Available { get; init; }
    public string? Reason { get; init; }
    public string? PatientMessage { get; init; }
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class ContextSnapshotDto
{
    public string SnapshotRef { get; init; } = string.Empty;
    public string CacheStatus { get; init; } = "generated";
    public string CacheBackend { get; init; } = "in_memory";
    public DateTimeOffset ExpiresAt { get; init; }
    public string Watermark { get; init; } = string.Empty;
}
