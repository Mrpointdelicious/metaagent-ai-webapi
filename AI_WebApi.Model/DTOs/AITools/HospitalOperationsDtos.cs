using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using AI_WebApi.Model.AITools;

namespace AI_WebApi.Model.DTOs.AITools;

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class HospitalOperationsQueryRequestDto
{
    public string Scope { get; init; } = "institution";
    public int? HospitalId { get; init; }
    public string? HospitalName { get; init; }
    public IReadOnlyList<int> HospitalIds { get; init; } = Array.Empty<int>();
    public string Section { get; init; } = "all";
    public string? StartDate { get; init; }
    public string? EndDate { get; init; }
    public string OutputMode { get; init; } = "analysis";
    public string AnalysisDepth { get; init; } = "standard";
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class HospitalOperationsToolResponseDto
{
    public string ToolName { get; init; } =
        "query_hospital_operations";
    public string ContractVersion { get; init; } =
        HospitalOperationsContract.Version;
    public string RequestId { get; init; } = string.Empty;
    public string Status { get; init; } = "success";
    public HospitalOperationsResultDto? Data { get; init; }
    public IReadOnlyList<string> ReasonCodes { get; init; } =
        Array.Empty<string>();
    public string? AgentSummary { get; init; }
    public DateTimeOffset GeneratedAt { get; init; } = DateTimeOffset.UtcNow;
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class HospitalOperationsResultDto
{
    public string Scope { get; init; } = "institution";
    public string Section { get; init; } = "all";
    public string AnalysisDepth { get; init; } = "standard";
    public HospitalOperationsPeriodDto Period { get; init; } = new();
    public IReadOnlyList<HospitalOperationsSnapshotDto> Hospitals { get; init; } =
        Array.Empty<HospitalOperationsSnapshotDto>();
    public HospitalOperationsPairwiseComparisonDto? Comparison { get; init; }
    public IReadOnlyList<HospitalOperationsAnalysisItemDto> Analysis { get; init; } =
        Array.Empty<HospitalOperationsAnalysisItemDto>();
    public HospitalOperationsReportArtifactDto? Report { get; set; }
    public IReadOnlyList<string> DataNotes { get; init; } = Array.Empty<string>();
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class HospitalOperationsPeriodDto
{
    public string StartDate { get; init; } = string.Empty;
    public string EndDate { get; init; } = string.Empty;
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class HospitalDirectoryEntryDto
{
    public int HospitalId { get; init; }
    public string HospitalName { get; init; } = string.Empty;
    public int TenantLevel { get; init; }
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class HospitalOperationsSnapshotDto
{
    public HospitalDirectoryEntryDto Hospital { get; init; } = new();
    public HospitalOverviewDto? Overview { get; init; }
    public HospitalConsultationDto? Consultation { get; init; }
    public HospitalRoomsDto? Rooms { get; init; }
    public HospitalAssessmentTrainingDto? AssessmentTraining { get; init; }
    public HospitalRecordsDto? Records { get; init; }
    public IReadOnlyList<HospitalOperationsMetricDto> DerivedMetrics { get; init; } =
        Array.Empty<HospitalOperationsMetricDto>();
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class HospitalOverviewDto
{
    public int PatientCount { get; init; }
    public int DoctorCount { get; init; }
    public int DailyActiveCount { get; init; }
    public int PlatformHospitalCount { get; init; }
    public int PlatformSchoolCount { get; init; }
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class HospitalConsultationDto
{
    public int ConsultationPeople { get; init; }
    public int ConsultationTimes { get; init; }
    public string TimeScope { get; init; } = "query_window";
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class HospitalRoomsDto
{
    public int ConsultationRooms { get; init; }
    public int AssessmentRooms { get; init; }
    public int TrainingRooms { get; init; }
    public int TotalRooms => ConsultationRooms + AssessmentRooms + TrainingRooms;
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class HospitalAssessmentTrainingDto
{
    public int AssessmentCount { get; init; }
    public int ScaleAssessmentCount { get; init; }
    public int ParadigmAssessmentCount { get; init; }
    public int OfflineTrainingCount { get; init; }
    public int DeviceTrainingCount { get; init; }
    public int CorridorTrainingCount { get; init; }
    public string TimeScope { get; init; } = "upstream_snapshot";
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class HospitalRecordsDto
{
    public int RealConsultationCount { get; init; }
    public int AiConsultationCount { get; init; }
    public int RemoteConsultationCount { get; init; }
    public int MedicalRecordCount { get; init; }
    public int PrescriptionTemplateCount { get; init; }
    public int ReportCount { get; init; }
    public string TimeScope { get; init; } = "upstream_snapshot";
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class HospitalOperationsMetricDto
{
    public string Code { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public decimal? Value { get; init; }
    public string Unit { get; init; } = string.Empty;
    public string DisplayValue { get; init; } = string.Empty;
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class HospitalOperationsAnalysisItemDto
{
    public string Code { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Text { get; init; } = string.Empty;
    public int? HospitalId { get; init; }
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class HospitalOperationsPairwiseComparisonDto
{
    public HospitalDirectoryEntryDto LeftHospital { get; init; } = new();
    public HospitalDirectoryEntryDto RightHospital { get; init; } = new();
    public string DifferenceDefinition { get; init; } = "right_minus_left";
    public IReadOnlyList<HospitalOperationsComparisonMetricDto> Metrics { get; init; } =
        Array.Empty<HospitalOperationsComparisonMetricDto>();
    public IReadOnlyList<string> Notes { get; init; } = Array.Empty<string>();
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class HospitalOperationsComparisonMetricDto
{
    public string Section { get; init; } = string.Empty;
    public string Code { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public decimal LeftValue { get; init; }
    public decimal RightValue { get; init; }
    public string LeftDisplayValue { get; init; } = string.Empty;
    public string RightDisplayValue { get; init; } = string.Empty;
    public decimal Difference { get; init; }
    public string DifferenceDisplayValue { get; init; } = string.Empty;
    public string Unit { get; init; } = string.Empty;
    public string DifferenceUnit { get; init; } = string.Empty;
    public string TimeScope { get; init; } = string.Empty;
    public int? LargerValueHospitalId { get; init; }
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class HospitalOperationsReportArtifactDto
{
    public string ArtifactRef { get; init; } = string.Empty;
    public string StorageMode { get; init; } = "durable_filesystem";
    public string HtmlUrl { get; init; } = string.Empty;
    public string ImageUrl { get; init; } = string.Empty;
    public string DisplayMarkdown { get; init; } = string.Empty;
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public bool Reused { get; init; }
    public string QueryFingerprint { get; init; } = string.Empty;
    public string ContentFingerprint { get; init; } = string.Empty;
    public string AnalysisRuleVersion { get; init; } = string.Empty;
    public string TemplateVersion { get; init; } = string.Empty;
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class HospitalOperationsArtifactIdentityDto
{
    public string ArtifactRef { get; init; } = string.Empty;
    public string QueryFingerprint { get; init; } = string.Empty;
    public string ContentFingerprint { get; init; } = string.Empty;
    public string ContractVersion { get; init; } = string.Empty;
    public string AnalysisRuleVersion { get; init; } = string.Empty;
    public string TemplateVersion { get; init; } = string.Empty;
    public string RenderProfile { get; init; } = string.Empty;
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class HospitalOperationsArtifactManifestDto
{
    public string ArtifactRef { get; init; } = string.Empty;
    public string ArtifactVersion { get; init; } = "v2";
    public string QueryFingerprint { get; init; } = string.Empty;
    public string ContentFingerprint { get; init; } = string.Empty;
    public string ContractVersion { get; init; } = string.Empty;
    public string AnalysisRuleVersion { get; init; } = string.Empty;
    public string TemplateVersion { get; init; } = string.Empty;
    public string RenderProfile { get; init; } = string.Empty;
    public DateTimeOffset CreatedAt { get; init; }
    public string StorageMode { get; init; } = "durable_filesystem";
    public IReadOnlyList<string> Formats { get; init; } =
        new[] { "html", "png" };
}

public sealed class HospitalOperationsArtifactWriteResult
{
    public HospitalOperationsArtifactManifestDto Manifest { get; init; } = new();
    public bool Reused { get; init; }
}

public enum HospitalReportAccessStatus
{
    Available,
    Invalid,
    Missing
}

public sealed class HospitalReportAccessResult
{
    public HospitalReportAccessStatus Status { get; init; }
    public byte[] Bytes { get; init; } = Array.Empty<byte>();
    public string MimeType { get; init; } = "application/octet-stream";
}
