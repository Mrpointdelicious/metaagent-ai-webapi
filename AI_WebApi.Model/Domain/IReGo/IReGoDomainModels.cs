using AI_WebApi.Model.DTOs.AITools;
using AI_WebApi.Model.Entities;

namespace AI_WebApi.Model.Domain.IReGo;

public sealed class TrustedPatientContext
{
    public int PatientId { get; init; }
    public string SubjectRef { get; init; } = string.Empty;
    public string ConversationId { get; init; } = string.Empty;
    public string TraceId { get; init; } = string.Empty;

    public string PatientRef => $"patient:{PatientId}";
    public string ContextKey => $"{SubjectRef}|{PatientRef}";
}

public sealed class IReGoRawDataSet
{
    public PatientEntity? Patient { get; init; }
    public IReadOnlyList<RehabPlanEntity> Plans { get; init; } = Array.Empty<RehabPlanEntity>();
    public IReadOnlyDictionary<int, DeviceEntity> Devices { get; init; } =
        new Dictionary<int, DeviceEntity>();
    public IReadOnlyDictionary<int, RehabTemplateEntity> Templates { get; init; } =
        new Dictionary<int, RehabTemplateEntity>();
    public IReadOnlyList<RehabReportEntity> Reports { get; init; } = Array.Empty<RehabReportEntity>();
    public IReadOnlySet<int> ExecutionEvidencePlanIds { get; init; } =
        new HashSet<int>();
    public int ReferenceAnomalyCount { get; init; }
    public string SourceSystem { get; init; } = "robotdb_main";
    public string SourceCompleteness { get; init; } = "authoritative_primary";
}

public sealed class TrainingStateResult
{
    public string State { get; init; } = "execution_unknown";
    public string Confidence { get; init; } = "low";
    public IReadOnlyList<ToolIssueDto> Issues { get; init; } = Array.Empty<ToolIssueDto>();
}

public sealed class ParsedIReGoReport
{
    public bool ParseSucceeded { get; init; }
    public bool HasReportList { get; init; }
    public string SchemaFamily { get; init; } = "unknown";
    public string SchemaSignature { get; init; } = string.Empty;
    public IReadOnlyList<IReGoContentBlockDto> DirectTraining { get; init; } =
        Array.Empty<IReGoContentBlockDto>();
    public IReadOnlyList<IReGoContentBlockDto> Games { get; init; } =
        Array.Empty<IReGoContentBlockDto>();
    public IReadOnlyList<IReGoContentBlockDto> Assessments { get; init; } =
        Array.Empty<IReGoContentBlockDto>();
    public IReadOnlyList<ToolIssueDto> Issues { get; init; } = Array.Empty<ToolIssueDto>();
    public int ReportItemCount { get; init; }
    public int ReportBlockCount { get; init; }
    public int ValidMetricCount { get; init; }
    public int ExcludedMetricCount { get; init; }
    public IReadOnlyList<string> ContentTypes { get; init; } = Array.Empty<string>();
}

public sealed class ParsedIReGoPlan
{
    public bool ParseSucceeded { get; init; }
    public string SchemaFamily { get; init; } = "unknown";
    public IReadOnlyList<IReGoContentBlockDto> Blocks { get; init; } =
        Array.Empty<IReGoContentBlockDto>();
    public IReadOnlyList<ToolIssueDto> Issues { get; init; } =
        Array.Empty<ToolIssueDto>();
}

public sealed class IReGoEvaluatedSession
{
    public RehabPlanEntity Plan { get; init; } = new();
    public RehabTemplateEntity Template { get; init; } = new();
    public DeviceEntity Device { get; init; } = new();
    public RehabReportEntity Report { get; init; } = new();
    public TrainingStateResult State { get; init; } = new();
    public ParsedIReGoReport ParsedReport { get; init; } = new();
    public bool Usable { get; init; }
    public bool EligibleForLatest { get; init; } = true;
    public string ValidationStatus { get; init; } = "invalid";
    public IReadOnlyList<ToolIssueDto> Issues { get; init; } = Array.Empty<ToolIssueDto>();
}

public sealed class IReGoEvaluatedPlan
{
    public RehabPlanEntity Plan { get; init; } = new();
    public RehabTemplateEntity Template { get; init; } = new();
    public DeviceEntity Device { get; init; } = new();
    public TrainingStateResult State { get; init; } = new();
    public ParsedIReGoPlan ParsedPlan { get; init; } = new();
    public IReGoEvaluatedSession? EffectiveSession { get; init; }
    public bool IsVoided { get; init; }
    public IReadOnlyList<ToolIssueDto> Issues { get; init; } =
        Array.Empty<ToolIssueDto>();
}

public sealed class IReGoPatientEvaluation
{
    public IReGoRawDataSet Raw { get; init; } = new();
    public IReadOnlyList<IReGoEvaluatedSession> Sessions { get; init; } =
        Array.Empty<IReGoEvaluatedSession>();
    public IReadOnlyList<IReGoEvaluatedPlan> Plans { get; init; } =
        Array.Empty<IReGoEvaluatedPlan>();
    public int StrictPlanCount { get; init; }
    public int EligiblePlanCount { get; init; }
    public int VoidedPlanCount { get; init; }
    public int ReferenceAnomalyCount { get; init; }
    public int StrictReportCount { get; init; }
    public string DataState { get; init; } = "none";
    public IReadOnlyList<ToolIssueDto> Issues { get; init; } = Array.Empty<ToolIssueDto>();
    public string Watermark { get; init; } = string.Empty;
}

public sealed class IReGoSessionBinding
{
    public string SessionRef { get; init; } = string.Empty;
    public string ContextKey { get; init; } = string.Empty;
    public int PlanId { get; init; }
    public int ReportId { get; init; }
    public string SchemaSignature { get; init; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; init; }
}

public sealed class PatientContextSnapshot
{
    public string SnapshotRef { get; init; } = string.Empty;
    public string ContextKey { get; init; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; init; }
    public string Watermark { get; init; } = string.Empty;
    public MultisourcePatientContextDto Data { get; init; } = new();
    public IReadOnlyDictionary<string, IReGoSessionBinding> SessionBindings { get; init; } =
        new Dictionary<string, IReGoSessionBinding>();
}

public sealed class IReGoMetricEvaluationInput
{
    public string MetricCode { get; init; } = string.Empty;
    public string MetricLabel { get; init; } = string.Empty;
    public string? Unit { get; init; }
    public string UnitStatus { get; init; } = "unknown";
    public IReadOnlyList<decimal> Values { get; init; } = Array.Empty<decimal>();
    public IReadOnlyDictionary<string, decimal?> Conditions { get; init; } =
        new Dictionary<string, decimal?>();
}

public sealed class IReGoCapabilityDefinition
{
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string BetterDirection { get; init; } = "descriptive_only";
}

public sealed class IReGoTrainingModeEvaluationInput
{
    public string RehabContent { get; init; } = string.Empty;
    public IReadOnlyList<IReGoTrainingModeObservation> Observations
        { get; init; } = Array.Empty<IReGoTrainingModeObservation>();
}

public sealed class IReGoTrainingModeObservation
{
    public int ReportOrdinal { get; init; }
    public bool IsComplete { get; init; }
    public int? ModeCode { get; init; }
    public string? ModeName { get; init; }
    public decimal? Unloading { get; init; }
    public decimal? Resistance { get; init; }
}

/// <summary>
/// Ordered observations for one identified rehabilitation game lane. Game
/// scores are normalized by effective training seconds before evaluation.
/// Difficulty is treated as an ordinal product setting, not a clinical scale.
/// </summary>
public sealed class IReGoGamePerformanceEvaluationInput
{
    public string DisplayName { get; init; } = "训练游戏";
    public IReadOnlyList<IReGoGamePerformanceObservation> Observations
        { get; init; } = Array.Empty<IReGoGamePerformanceObservation>();
}

public sealed class IReGoGamePerformanceObservation
{
    public int ReportOrdinal { get; init; }
    public bool IsComplete { get; init; }
    public int? Difficulty { get; init; }
    public decimal? ScoreRate { get; init; }
}

/// <summary>
/// Three patient-facing layers. Basic is suitable for direct voice output;
/// explanation is returned when the patient asks why; life hint is a guarded
/// activity relevance statement and never a diagnosis or independence claim.
/// </summary>
public sealed class IReGoLayeredPatientMessage
{
    public string BasicEvaluation { get; init; } = string.Empty;
    public string ExtendedExplanation { get; init; } = string.Empty;
    public string LifeHint { get; init; } = string.Empty;
}
