using AI_WebApi.Model.DTOs.AITools;
using AI_WebApi.Model.Entities;

namespace AI_WebApi.Repository.AITools;

public interface IPatientLayerRepository
{
    Task<PatientEntity?> PatientAsync(int patientId, CancellationToken cancellationToken);
    Task<(int Count, IReadOnlyList<PatientMedicalRow> Rows)> MedicalAsync(int patientId, int page, int size, CancellationToken cancellationToken);
    Task<(int Count, IReadOnlyList<PatientConsultationRecordDto> Rows)> ConsultationsAsync(int patientId, int page, int size, CancellationToken cancellationToken);
    Task<IReadOnlyList<PatientPrescriptionDto>> PrescriptionsAsync(int patientId, int medicalId, CancellationToken cancellationToken);
    Task<(int Count, IReadOnlyList<PatientMedicalRow> Rows)> MedicalAsync(AI_WebApi.Model.Domain.IReGo.TrustedPatientContext context, int page, int size, CancellationToken ct) =>
        MedicalAsync(context.PatientId, page, size, ct);
    Task<(int Count, IReadOnlyList<PatientConsultationRecordDto> Rows)> ConsultationsAsync(AI_WebApi.Model.Domain.IReGo.TrustedPatientContext context, int page, int size, CancellationToken ct) =>
        ConsultationsAsync(context.PatientId, page, size, ct);
    Task<IReadOnlyList<PatientPrescriptionDto>> PrescriptionsAsync(AI_WebApi.Model.Domain.IReGo.TrustedPatientContext context, int medicalId, CancellationToken ct) =>
        PrescriptionsAsync(context.PatientId, medicalId, ct);
    Task<(int Count, IReadOnlyList<PatientRehabPlanDto> Rows)> PlansAsync(int patientId, int page, int size, CancellationToken cancellationToken);
    Task<(int Count, IReadOnlyList<PatientRehabPlanDto> Rows)> PlansAsync(AI_WebApi.Model.Domain.IReGo.TrustedPatientContext context, int page, int size, CancellationToken cancellationToken) =>
        PlansAsync(context.PatientId, page, size, cancellationToken);
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
