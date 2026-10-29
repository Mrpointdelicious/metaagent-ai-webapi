using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace AI_WebApi.Model.DTOs.AITools;

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class GetIReTourSessionAnalysisRequestDto
{
    public DifySystemContextDto? SystemContext { get; init; }
    public string? DataScope { get; init; }
    public string Selector { get; init; } = "latest_usable";
    public string? SessionRef { get; init; }
    public string DetailLevel { get; init; } = "standard";
    public string QualityDetail { get; init; } = "summary";
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class IReTourSessionAnalysisDto
{
    public string SourceSnapshot { get; set; } = string.Empty;
    public IReTourReadingDto Interpretation { get; set; } = new();
    public IReTourSessionIdentityDto Session { get; init; } = new();
    public IReTourSessionTimeDto Time { get; init; } = new();
    public IReTourActivityBriefDto Activity { get; init; } = new();
    public IReTourSessionPlanDto Plan { get; init; } = new();
    public IReTourSessionResultDto Result { get; init; } = new();
    public IReTourSessionQualityDto Quality { get; init; } = new();
    public IReadOnlyList<IReTourSourceFactDto> Sources { get; init; } =
        Array.Empty<IReTourSourceFactDto>();
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class IReTourSessionIdentityDto
{
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public RecordTenantDto? RecordTenant { get; set; }
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public string? OwnerUserRef { get; set; }
    public string SessionRef { get; init; } = string.Empty;
    public string PatientRef { get; init; } = string.Empty;
    public string DeviceFamily { get; init; } = "iretour";
    public string SourceSystem { get; init; } = "project_iretour";
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class IReTourSessionTimeDto
{
    public DateTimeOffset? ExecutionStartedAt { get; init; }
    public DateTimeOffset? ExecutionEndedAt { get; init; }
    public DateTimeOffset? SourceRecordCreatedAt { get; init; }
    public DateTimeOffset? SourceParsedAt { get; init; }
    public string TrainingState { get; init; } = "execution_unknown";
    public string StateConfidence { get; init; } = "low";
    public string TimeZone { get; init; } = "Asia/Shanghai";
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class IReTourSessionPlanDto
{
    public string SemanticsStatus { get; init; } = "observed_settings_not_verified_prescription";
    public string DurationSourceField { get; init; } = "dbwalk.duration";
    public string ParametersSource { get; init; } = "walkreportdetails.details";
    public int? DurationSeconds { get; init; }
    public IReadOnlyList<IReTourPlanParameterDto> Parameters { get; init; } =
        Array.Empty<IReTourPlanParameterDto>();
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class IReTourPlanParameterDto
{
    public string Code { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string? Value { get; init; }
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class IReTourSessionResultDto
{
    public string ResultState { get; init; } = "no_result";
    public IReadOnlyList<IReTourMetricDto> Metrics { get; init; } =
        Array.Empty<IReTourMetricDto>();
    public IReTourRawEvidenceDto RawEvidence { get; init; } = new();
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class IReTourMetricDto
{
    public string SourceField { get; init; } = string.Empty;
    public string UnitStatus { get; init; } = "unconfirmed";
    public string MetricCode { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public decimal? RawValue { get; init; }
    public decimal? NormalizedValue { get; init; }
    public string? Unit { get; init; }
    public string ValueStatus { get; init; } = "valid";
    public string RegistryVersion { get; init; } = string.Empty;
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class IReTourRawEvidenceDto
{
    public bool Available { get; init; }
    public IReadOnlyList<string> SourceTypes { get; init; } = Array.Empty<string>();
    public bool ReturnedToModel { get; init; }
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class IReTourSessionQualityDto
{
    public bool Usable { get; init; } = true;
    public int IssueCount { get; init; }
    public IReadOnlyList<ToolIssueDto> Issues { get; init; } = Array.Empty<ToolIssueDto>();
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class IReTourSourceFactDto
{
    public string SourceTable { get; init; } = string.Empty;
    public string LinkMethod { get; init; } = string.Empty;
    public string LinkStatus { get; init; } = string.Empty;
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class IReTourReadingDto
{
    public string Method { get; init; } = "deterministic_descriptive_v1";
    public string Summary { get; init; } = string.Empty;
    public IReadOnlyList<string> Observations { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Limitations { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> NextChecks { get; init; } = Array.Empty<string>();
}
