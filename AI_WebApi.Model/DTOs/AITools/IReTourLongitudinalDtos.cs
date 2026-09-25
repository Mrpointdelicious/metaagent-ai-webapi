using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace AI_WebApi.Model.DTOs.AITools;

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class GetIReTourLongitudinalAnalysisRequestDto
{
    public DifySystemContextDto? SystemContext { get; init; }
    public string SelectionMode { get; init; } = "latest_count";
    public int ReportCount { get; init; } = 4;
    public int? StartOrdinal { get; init; }
    public int? EndOrdinal { get; init; }
    public DateTimeOffset? StartDate { get; init; }
    public DateTimeOffset? EndDate { get; init; }
    public string ActivityScope { get; init; } = "straight_primary";
    public string DetailLevel { get; init; } = "standard";
    public string QualityDetail { get; init; } = "summary";
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class IReTourLongitudinalAnalysisDto
{
    public string PatientRef { get; init; } = string.Empty;
    public IReTourLongitudinalWindowDto Window { get; init; } = new();
    public IReadOnlyList<IReTourLongitudinalSessionDto> Sessions { get; init; } =
        Array.Empty<IReTourLongitudinalSessionDto>();
    public IReadOnlyList<IReTourMetricSeriesDto> MetricSeries { get; init; } =
        Array.Empty<IReTourMetricSeriesDto>();
    public ToolIssueDto? Quality { get; init; }
    public IReadOnlyList<string> AnswerableQuestions { get; init; } =
        Array.Empty<string>();
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class IReTourLongitudinalWindowDto
{
    public string SelectionMode { get; init; } = string.Empty;
    public int RequestedCount { get; init; }
    public int ResolvedCount { get; init; }
    public int ComparableCount { get; init; }
    public DateTimeOffset? StartedAt { get; init; }
    public DateTimeOffset? EndedAt { get; init; }
    public bool IsContiguous { get; init; }
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class IReTourLongitudinalSessionDto
{
    public int Ordinal { get; init; }
    public string SessionRef { get; init; } = string.Empty;
    public DateTimeOffset? SessionTime { get; init; }
    public string CanonicalCode { get; init; } = string.Empty;
    public string TrainingState { get; init; } = "execution_unknown";
    public string ResultState { get; init; } = "no_result";
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class IReTourMetricSeriesDto
{
    public string MetricCode { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string? Unit { get; init; }
    public IReadOnlyList<IReTourMetricPointDto> Points { get; init; } =
        Array.Empty<IReTourMetricPointDto>();
    public decimal? FirstValue { get; init; }
    public decimal? LastValue { get; init; }
    public decimal? AbsoluteChange { get; init; }
    public decimal? RelativeChange { get; init; }
    public string Direction { get; init; } = "unavailable";
    public string InterpretationStatus { get; init; } = "not_evaluated";
    public string RegistryVersion { get; init; } = string.Empty;
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class IReTourMetricPointDto
{
    public int Ordinal { get; init; }
    public string SessionRef { get; init; } = string.Empty;
    public DateTimeOffset? SessionTime { get; init; }
    public decimal? Value { get; init; }
    public string ValueStatus { get; init; } = "missing";
}
