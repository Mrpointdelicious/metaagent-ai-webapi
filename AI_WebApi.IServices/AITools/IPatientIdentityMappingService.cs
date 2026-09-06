using AI_WebApi.Model.DTOs.AITools;

namespace AI_WebApi.IServices.AITools;

public interface IPatientIdentityMappingService
{
    Task<ToolResponseDto<PatientIdentityResolutionDto>> ResolveAsync(
        int projectPatientId,
        string requestId,
        CancellationToken cancellationToken = default);

    Task<ToolResponseDto<PatientIdentityResolutionDto>> ResolveByPhoneAsync(
        string phone,
        string requestId,
        CancellationToken cancellationToken = default);
}
