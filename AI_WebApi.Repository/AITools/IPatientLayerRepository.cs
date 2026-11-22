using AI_WebApi.Model.DTOs.AITools;
using AI_WebApi.Model.Domain.IReGo;
using AI_WebApi.Model.Entities;

namespace AI_WebApi.Repository.AITools;

public interface IPatientLayerRepository
{
    Task<PatientEntity?> PatientAsync(TrustedPatientContext context, CancellationToken ct);
    Task<(int Count, IReadOnlyList<PatientMedicalRow> Rows)> MedicalAsync(TrustedPatientContext context, int page, int size, CancellationToken ct);
    Task<(int Count, IReadOnlyList<PatientConsultationRecordDto> Rows)> ConsultationsAsync(TrustedPatientContext context, int page, int size, CancellationToken ct);
    Task<IReadOnlyList<PatientPrescriptionDto>> PrescriptionsAsync(TrustedPatientContext context, int medicalId, CancellationToken ct);
    Task<(int Count, IReadOnlyList<PatientRehabPlanDto> Rows)> PlansAsync(TrustedPatientContext context, int page, int size, CancellationToken ct);
    Task<(int Count, IReadOnlyList<PatientPlanReportDto> Rows)> ReportsAsync(TrustedPatientContext context, int page, int size, CancellationToken ct);
}

public sealed class PatientMedicalRow
{
    public int? InstitutionId { get; set; }
    public RecordTenantDto? RecordTenant { get; set; }
    public int RecordId { get; set; }
    public DateTime? RecordedAt { get; set; }
    public string? MedicalRecord { get; set; }
    public string? MedicalContent { get; set; }
    public string? Summary { get; set; }
    public string? FollowupTime { get; set; }
    public string? FollowupHospital { get; set; }
}
