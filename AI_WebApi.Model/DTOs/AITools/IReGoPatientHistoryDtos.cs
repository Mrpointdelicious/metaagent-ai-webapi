using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace AI_WebApi.Model.DTOs.AITools;

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class GetIReGoPatientHistoryRequestDto
{
    public DifySystemContextDto? SystemContext { get; init; }
    public string? DataScope { get; init; }
    public int PageNumber { get; init; } = 1;
    public int PageSize { get; init; } = 10;
    public string RecordScope { get; init; } = "all";
    public string ProjectScope { get; init; } = "all";
    public string TrainingState { get; init; } = "all";
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class IReGoPatientHistoryDto
{
    public string PatientRef { get; init; } = string.Empty;
    public IReGoHistoryPageDto Page { get; init; } = new();
    public IReGoHistorySummaryDto Summary { get; init; } = new();
    public IReadOnlyList<IReGoHistoryItemDto> Items { get; init; } =
        Array.Empty<IReGoHistoryItemDto>();
    public IReadOnlyList<string> AnswerableQuestions { get; init; } =
        Array.Empty<string>();
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class IReGoHistoryPageDto
{
    public int PageNumber { get; init; }
    public int PageSize { get; init; }
    public int TotalItems { get; init; }
    public int TotalPages { get; init; }
    public bool HasPrevious { get; init; }
    public bool HasNext { get; init; }
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class IReGoHistorySummaryDto
{
    public int PlanCount { get; init; }
    public int ReportCount { get; init; }
    public int CompleteReportCount { get; init; }
    public int IncompletePlanCount { get; init; }
    public int FuturePlanCount { get; init; }
    public int VoidedPlanCount { get; init; }
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class IReGoHistoryItemDto
{
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public RecordTenantDto? RecordTenant { get; set; }
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public string? OwnerUserRef { get; set; }
    public int Ordinal { get; init; }
    public string RecordType { get; init; } = "plan";
    public string PlanRef { get; init; } = string.Empty;
    public string? SessionRef { get; init; }
    public DateTimeOffset? SessionTime { get; init; }
    public string TrainingState { get; init; } = "execution_unknown";
    public string ReportStatus { get; init; } = "not_available";
    public IReadOnlyList<string> ContentTypes { get; init; } =
        Array.Empty<string>();
    public IReadOnlyList<string> DisplayNames { get; init; } =
        Array.Empty<string>();
    public IReGoHistoryEvaluationAvailabilityDto EvaluationAvailability
        { get; init; } = new();
    public string PatientMessage { get; init; } = string.Empty;
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class IReGoHistoryEvaluationAvailabilityDto
{
    public bool SingleSession { get; init; }
    public bool Longitudinal { get; init; }
    public IReadOnlyList<string> EligibleCapabilities { get; init; } =
        Array.Empty<string>();
    public string Boundary { get; init; } =
        "既往记录查询只用于发现可查询记录，不在此接口中生成改善、持平或下降结论。";
}
