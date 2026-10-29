using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace AI_WebApi.Model.DTOs.AITools;

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class GetIReTourPatientHistoryRequestDto
{
    public DifySystemContextDto? SystemContext { get; init; }
    public string? DataScope { get; init; }
    public int PageNumber { get; init; } = 1;
    public int PageSize { get; init; } = 10;
    public string RecordScope { get; init; } = "all";
    public string ActivityScope { get; init; } = "all";
    public string TrainingState { get; init; } = "all";
    public string ResultState { get; init; } = "all";
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class IReTourPatientHistoryDto
{
    public string PatientRef { get; init; } = string.Empty;
    public IReTourHistoryPageDto Page { get; init; } = new();
    public IReTourHistorySummaryDto Summary { get; init; } = new();
    public IReadOnlyList<IReTourHistoryItemDto> Items { get; init; } =
        Array.Empty<IReTourHistoryItemDto>();
    public IReadOnlyList<string> AnswerableQuestions { get; init; } =
        Array.Empty<string>();
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class IReTourHistoryPageDto
{
    public int PageNumber { get; init; }
    public int PageSize { get; init; }
    public int TotalItems { get; init; }
    public int TotalPages { get; init; }
    public bool HasPrevious { get; init; }
    public bool HasNext { get; init; }
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class IReTourHistorySummaryDto
{
    public int SessionCount { get; init; }
    public int SummaryAvailableCount { get; init; }
    public int RawOnlyCount { get; init; }
    public int NoResultCount { get; init; }
    public int QualityBlockedCount { get; init; }
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class IReTourHistoryItemDto
{
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public RecordTenantDto? RecordTenant { get; set; }
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public string? OwnerUserRef { get; set; }
    public int Ordinal { get; init; }
    public string? SessionRef { get; init; }
    public DateTimeOffset? SessionTime { get; init; }
    public IReTourActivityBriefDto Activity { get; init; } = new();
    public string TrainingState { get; init; } = "execution_unknown";
    public string ResultState { get; init; } = "no_result";
    public bool SingleSessionAvailable { get; init; }
    public bool LongitudinalAvailable { get; init; }
    public string? PatientMessage { get; init; }
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class IReTourActivityBriefDto
{
    public int? ItemId { get; init; }
    public string CanonicalCode { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string RegistryVersion { get; init; } = string.Empty;
}
