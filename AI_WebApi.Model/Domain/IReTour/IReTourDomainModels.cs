using AI_WebApi.Model.DTOs.AITools;

namespace AI_WebApi.Model.Domain.IReTour;

public static class IReTourDomainConstants
{
    public const string UnknownItemCode = "unknown_item";
}

public static class IReTourExecutionStates
{
    public const string Completed = "completed";
    public const string NotStarted = "not_started";
    public const string NotCompleted = "not_completed";
    public const string Interrupted = "interrupted";
    public const string ExecutionUnknown = "execution_unknown";
}

public static class IReTourResultStates
{
    public const string SummaryAvailable = "summary_available";
    public const string RawOnly = "raw_only";
    public const string NoResult = "no_result";
    public const string ResultConflicting = "result_conflicting";
    public const string ResultUnusable = "result_unusable";
}

/// <summary>Resolved, verified patient binding for a trusted patient context.</summary>
public sealed class IReTourPatientBinding
{
    public bool Verified { get; init; }
    public string SourceUserNamespace { get; init; } = "dbuser";
    public string BindingMethod { get; init; } = "direct_primary_key";
    public int BindingVersion { get; init; } = 1;
    public bool Ambiguous { get; init; }
    public int? BusinessId { get; init; }
}

public sealed class IReTourReportLink
{
    /// <summary>true when the summary report links to the walk by verified binding or deterministic FK.</summary>
    public bool SummaryVerified { get; init; }

    public bool SummaryAmbiguous { get; init; }
    public bool SummaryConflict { get; init; }
    public int SummaryRowCount { get; init; }
    public bool HasRawWalk { get; init; }
    public bool HasCognitive { get; init; }
    public bool RawLinkVerified { get; init; }
    public bool CognitiveLinkVerified { get; init; }
}

public sealed class IReTourParsedTimeSet
{
    public DateTimeOffset? ExecutionStartedAt { get; init; }
    public DateTimeOffset? ExecutionEndedAt { get; init; }
    public DateTimeOffset? SourceRecordCreatedAt { get; init; }
    public DateTimeOffset? SourceParsedAt { get; init; }
    public IReadOnlyList<ToolIssueDto> Issues { get; init; } = Array.Empty<ToolIssueDto>();
}

/// <summary>Result of the shared evaluation pipeline for one patient.</summary>
public sealed class IReTourEvaluationResult
{
    public bool BindingVerified { get; init; }
    public bool PatientExists { get; init; }
    public IReadOnlyList<IReTourEvaluatedSession> Sessions { get; init; } =
        Array.Empty<IReTourEvaluatedSession>();
    public IReadOnlyDictionary<int, IReadOnlyList<Entities.IReTourSummaryReportEntity>>
        SummaryReportsByWalk { get; init; } =
        new Dictionary<int, IReadOnlyList<Entities.IReTourSummaryReportEntity>>();
}

public sealed class IReTourMetricFact
{
    public string MetricCode { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public decimal? RawValue { get; init; }
    public decimal? NormalizedValue { get; init; }
    public string? Unit { get; init; }
    public string ValueStatus { get; init; } = "valid";
    public string SourceField { get; init; } = string.Empty;
}

/// <summary>
/// Fully evaluated IReTour session: canonical key, verified binding, device
/// gate, item registry entry, parsed time, execution/result states, metrics
/// and quality issues. Only sessions passing the patient-visible gate receive
/// a stable session_ref.
/// </summary>
public sealed class IReTourEvaluatedSession
{
    public string CanonicalSessionKey { get; init; }
    public int WalkId { get; init; }
    public int PatientId { get; init; }
    public int? BusinessId { get; init; }
    public string PatientBindingNamespace { get; init; }
    public int PatientBindingVersion { get; init; }
    public int ItemId { get; init; }
    public string CanonicalCode { get; init; }
    public string SchemaFamily { get; init; }
    public string DisplayName { get; init; }
    public bool DeviceVerified { get; init; }
    public IReTourParsedTimeSet Time { get; init; }
    public string ExecutionState { get; init; }
    public string ResultState { get; init; }
    public int? DurationSeconds { get; init; }
    public IReadOnlyList<IReTourMetricFact> Metrics { get; init; } =
        Array.Empty<IReTourMetricFact>();
    public IReadOnlyList<ToolIssueDto> Issues { get; init; } =
        Array.Empty<ToolIssueDto>();
    public bool RawEvidenceAvailable { get; init; }
    public IReadOnlyList<string> RawSourceTypes { get; init; } = Array.Empty<string>();
    public int? SourceWalkUserId { get; init; }

    /// <summary>Patient-visible gate: binding, device, item, time and result linkage all pass.</summary>
    public bool Visible =>
        DeviceVerified &&
        CanonicalCode != IReTourDomainConstants.UnknownItemCode &&
        !Issues.Any(x => x.Severity == "error");

    public bool UsableForLatest =>
        Visible &&
        ResultState == IReTourResultStates.SummaryAvailable &&
        ExecutionState != IReTourExecutionStates.ExecutionUnknown &&
        Metrics.Count > 0;

    public DateTimeOffset? SortTime =>
        Time.ExecutionStartedAt ?? Time.SourceRecordCreatedAt ?? Time.ExecutionEndedAt;
}
