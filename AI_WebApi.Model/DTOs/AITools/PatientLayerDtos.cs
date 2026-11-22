using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace AI_WebApi.Model.DTOs.AITools;

public static class PatientLayerContract
{
    public const string Version = "patient-layers-2.0.0";
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class PatientLayerRequestDto
{
    // Identity and scope are bound to the authenticated business session.
    public DifySystemContextDto? SystemContext { get; init; }
    public string? DataScope { get; init; }
    public int PageNumber { get; init; } = 1;
    public int PageSize { get; init; } = 5;
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class PatientProfileDto
{
    public string PatientRef { get; init; } = string.Empty;
    public IReadOnlyDictionary<string, object?> Profile { get; init; } = new Dictionary<string, object?>();
    public IReadOnlyDictionary<string, object?> CareContext { get; init; } = new Dictionary<string, object?>();
    public string Source { get; init; } = "project.dbuser.UserDetails";
    public IReadOnlyList<string> Limitations { get; init; } = Array.Empty<string>();
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class PatientConsultationDto
{
    public string PatientRef { get; init; } = string.Empty;
    public PatientLayerPageDto Page { get; init; } = new();
    public int MedicalRecordCount { get; init; }
    public int ConsultationCount { get; init; }
    public IReadOnlyList<PatientMedicalRecordDto> MedicalRecords { get; init; } = Array.Empty<PatientMedicalRecordDto>();
    public IReadOnlyList<PatientConsultationRecordDto> Consultations { get; init; } = Array.Empty<PatientConsultationRecordDto>();
    public IReadOnlyList<string> Limitations { get; init; } = Array.Empty<string>();
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class PatientMedicalRecordDto
{
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public RecordTenantDto? RecordTenant { get; set; }
    public int RecordId { get; init; }
    public DateTime? RecordedAt { get; init; }
    public string? MedicalRecord { get; init; }
    public IReadOnlyDictionary<string, object?> Content { get; init; } = new Dictionary<string, object?>();
    public string? Summary { get; init; }
    public string? FollowupTime { get; init; }
    public string? FollowupHospital { get; init; }
    public IReadOnlyList<PatientPrescriptionDto> Prescriptions { get; init; } = Array.Empty<PatientPrescriptionDto>();
    public string Source { get; init; } = "project.dbmedical";
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class PatientPrescriptionDto
{
    public int PrescriptionId { get; init; }
    public string? Name { get; init; }
    public string? Indication { get; init; }
    public string? Content { get; init; }
    public string? OfflineTraining { get; init; }
    public string? OnlineEvaluation { get; init; }
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class PatientConsultationRecordDto
{
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public RecordTenantDto? RecordTenant { get; set; }
    public int RecordId { get; init; }
    public int? MedicalId { get; init; }
    public DateTime? CreatedAt { get; init; }
    public DateTime? StartedAt { get; init; }
    public DateTime? EndedAt { get; init; }
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class PatientLayerPageDto
{
    public int PageNumber { get; init; }
    public int PageSize { get; init; }
    public int TotalItems { get; init; }
    public bool HasNext { get; init; }
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class PatientRehabDto
{
    public string PatientRef { get; init; } = string.Empty;
    public int PlanCount { get; init; }
    public IReadOnlyList<PatientRehabPlanDto> Plans { get; init; } = Array.Empty<PatientRehabPlanDto>();
    public PatientLayerPageDto PlanPage { get; init; } = new();
    public int ReportCount { get; init; }
    public IReadOnlyList<PatientPlanReportDto> Reports { get; init; } = Array.Empty<PatientPlanReportDto>();
    public PatientLayerPageDto ReportPage { get; init; } = new();
    public PatientRehabDeviceDto Iretour { get; init; } = new();
    public PatientRehabDeviceDto Irego { get; init; } = new();
    public IReadOnlyList<string> Limitations { get; init; } = Array.Empty<string>();
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class PatientRehabDeviceDto
{
    public string Availability { get; init; } = "unavailable";
    public object? History { get; init; }
    public IReadOnlyList<string> ReasonCodes { get; init; } = Array.Empty<string>();
    public string? PatientMessage { get; init; }
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class PatientPlanReportDto
{
    public string RecordRef { get; init; } = string.Empty;
    public int PlanId { get; init; }
    public DateTime? RecordedAt { get; init; }
    public RecordTenantDto RecordTenant { get; init; } = new();
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class PatientRehabPlanDto
{
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public RecordTenantDto? RecordTenant { get; set; }
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public string? OwnerUserRef { get; set; }
    public int PlanId { get; init; }
    public DateTime? CreatedAt { get; init; }
    public DateTime? ScheduledAt { get; init; }
    public int? CompletionFlag { get; init; }
    public int? Status { get; init; }
    public string? Details { get; init; }
    public string Source { get; init; } = "project.dbrehaplan";
}
