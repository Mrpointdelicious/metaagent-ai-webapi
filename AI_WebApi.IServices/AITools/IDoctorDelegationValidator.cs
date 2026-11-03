using AI_WebApi.Model.DTOs.AITools;

namespace AI_WebApi.IServices.AITools;

public interface IDoctorDelegationValidator
{
    Task<bool> ValidateAsync(DoctorDelegationDto grant, CancellationToken ct);
}
