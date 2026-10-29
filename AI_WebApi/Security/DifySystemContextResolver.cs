using AI_WebApi.Model.Domain.IReGo;
using AI_WebApi.Model.DTOs.AITools;

namespace AI_WebApi.Security;

public interface IDifySystemContextResolver
{
    TrustedPatientContextResolution Resolve(
        HttpContext httpContext,
        DifySystemContextDto? systemContext);
}

public sealed class TrustedPatientContextResolution
{
    public TrustedPatientContext? Context { get; init; }
    public int HttpStatusCode { get; init; } = StatusCodes.Status200OK;
    public string? ReasonCode { get; init; }
    public bool IsSuccess => Context != null;
}

/// <summary>
/// Reads the patient identity already authenticated and resolved by the scope filter.
/// </summary>
public sealed class DifySystemContextResolver : IDifySystemContextResolver
{
    public TrustedPatientContextResolution Resolve(
        HttpContext httpContext,
        DifySystemContextDto? systemContext)
    {
        if (httpContext.Items.TryGetValue(PatientV2ScopeFilter.ContextItemKey, out var scoped) && scoped is TrustedPatientContext trusted)
            return new TrustedPatientContextResolution { Context = trusted };
        return Failure(StatusCodes.Status401Unauthorized, "PATIENT_IDENTITY_PROOF_REQUIRED");
    }

    private static TrustedPatientContextResolution Failure(
        int httpStatusCode,
        string reasonCode) => new()
        {
            HttpStatusCode = httpStatusCode,
            ReasonCode = reasonCode
        };
}
