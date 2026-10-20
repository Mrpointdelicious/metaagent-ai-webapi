using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace AI_WebApi.Model.DTOs.AITools;

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class IReCogRequestDto
{
    public DifySystemContextDto? SystemContext { get; init; }
    public int PageNumber { get; init; } = 1;
    public int PageSize { get; init; } = 10;
    public string Selector { get; init; } = "latest_record";
    public string? SessionRef { get; init; }
    public string SelectionMode { get; init; } = "latest_count";
    public int ReportCount { get; init; } = 4;
    public DateTimeOffset? StartDate { get; init; }
    public DateTimeOffset? EndDate { get; init; }
    public string? ExpectedSourceSnapshot { get; init; }
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class IReCogSessionDto
{
    public string SessionRef { get; init; } = string.Empty;
    public DateTimeOffset? RecordedAt { get; init; }
    public int? SourceCompletionFlag { get; init; }
    public string ResultState { get; init; } = "no_result";
    public string? InstrumentRef { get; init; }
    public string? InstrumentTitle { get; init; }
    public string InstrumentVersionStatus { get; init; } = "unverified";
    public decimal? ReportedScore { get; init; }
    public string ScoreMeaning { get; init; } = "source_reported_total_only";
    public string SchemaFamily { get; init; } = "unrecognized";
    public string SourceField { get; init; } = "dbreport.ReportDetails.Score";
    public IReadOnlyList<string> ReasonCodes { get; init; } = Array.Empty<string>();
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class IReCogAnalysisDto
{
    public IReadOnlyDictionary<string, int> Counts { get; init; } = new Dictionary<string, int>();
    public string DeviceFamily { get; init; } = "irecog";
    public string SourceSystem { get; init; } = "project";
    public string SourceSnapshot { get; set; } = string.Empty;
    public IReadOnlyList<IReCogSessionDto> Sessions { get; init; } = Array.Empty<IReCogSessionDto>();
    public int PageNumber { get; init; }
    public int TotalItems { get; init; }
    public bool HasNext { get; init; }
    public string Comparability { get; init; } = "not_requested";
    public IReTourReadingDto Interpretation { get; init; } = new();
}
