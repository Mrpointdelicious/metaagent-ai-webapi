using AI_WebApi.Model.Domain.IReGo;
using AI_WebApi.Model.DTOs.AITools;

namespace AI_WebApi.IServices.AITools;

public interface IPatientLayerService
{
    Task<ToolResponseDto<PatientProfileDto>> ProfileAsync(TrustedPatientContext context, string requestId, CancellationToken cancellationToken);
    Task<ToolResponseDto<PatientConsultationDto>> ConsultationAsync(TrustedPatientContext context, PatientLayerRequestDto request, string requestId, CancellationToken cancellationToken);
    Task<ToolResponseDto<PatientRehabDto>> RehabAsync(TrustedPatientContext context, PatientLayerRequestDto request, string requestId, CancellationToken cancellationToken);
}
