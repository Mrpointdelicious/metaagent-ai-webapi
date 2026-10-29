using AI_WebApi.Model.Domain.IReGo;
using AI_WebApi.Model.DTOs.AITools;

namespace AI_WebApi.IServices.AITools;

public interface IPatientAccessContextResolver
{
    Task<PatientAccessResolution> ResolveAsync(DifySystemContextDto? input, string? dataScope, string requestId, CancellationToken ct);
}

public sealed record PatientAccessResolution(TrustedPatientContext? Context, string? ReasonCode = null, int HttpStatusCode = 200)
{
    public bool IsSuccess => Context != null;
}

public interface IPatientAccessContextAccessor
{
    PatientAccessContext? Current { get; set; }
}
