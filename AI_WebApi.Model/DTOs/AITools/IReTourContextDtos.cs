using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace AI_WebApi.Model.DTOs.AITools;

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class GetIReTourPatientContextRequestDto
{
    public DifySystemContextDto? SystemContext { get; init; }
    public string? DataScope { get; init; }
    public string ProjectionLevel { get; init; } = "compact";
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class IReTourPatientContextDto
{
    public string PatientRef { get; init; } = string.Empty;
    public IReTourContextDomainDto IReTour { get; init; } = new();
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class IReTourContextDomainDto
{
    public string Availability { get; init; } = "unavailable";
    public string DataState { get; init; } = "none";
    public IReTourContextCountsDto Counts { get; init; } = new();
    public IReadOnlyList<IReTourActivityCountDto> ActivitySummary { get; init; } =
        Array.Empty<IReTourActivityCountDto>();
    public IReadOnlyList<IReTourRecentSessionDto> RecentSessions { get; init; } =
        Array.Empty<IReTourRecentSessionDto>();
    public IReTourLatestSessionDto? Latest { get; init; }
    public IReTourQueryCapabilitiesDto QueryCapabilities { get; init; } = new();
    public IReadOnlyList<IReTourQueryCapabilityDto> QueryCapabilityDetails { get; init; } =
        Array.Empty<IReTourQueryCapabilityDto>();
    public IReTourSessionQualityDto Quality { get; init; } = new();
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class IReTourContextCountsDto
{
    public int SessionCount { get; init; }
    public int SummaryAvailableCount { get; init; }
    public int RawOnlyCount { get; init; }
    public int NoResultCount { get; init; }
    public int UsableSessionCount { get; init; }
    public int QualityBlockedCount { get; init; }
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class IReTourActivityCountDto
{
    public string CanonicalCode { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public int SessionCount { get; init; }
    public int UsableSessionCount { get; init; }
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class IReTourRecentSessionDto
{
    public int Ordinal { get; init; }
    public string SessionRef { get; init; } = string.Empty;
    public DateTimeOffset? SessionTime { get; init; }
    public IReTourActivityBriefDto Activity { get; init; } = new();
    public string TrainingState { get; init; } = "execution_unknown";
    public string ResultState { get; init; } = "no_result";
    public bool DetailAvailable { get; init; }
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class IReTourLatestSessionDto
{
    public string SessionRef { get; init; } = string.Empty;
    public DateTimeOffset? SessionTime { get; init; }
    public string TrainingState { get; init; } = "execution_unknown";
    public string ResultState { get; init; } = "no_result";
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class IReTourQueryCapabilitiesDto
{
    public bool LatestUsable { get; init; }
    public bool BySessionRef { get; init; }
    public bool Longitudinal { get; init; }
    public IReadOnlyList<string> BlockedReasonCodes { get; init; } =
        Array.Empty<string>();
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class IReTourQueryCapabilityDto
{
    public string Capability { get; init; } = string.Empty;
    public bool Available { get; init; }
    public string? Reason { get; init; }
}
