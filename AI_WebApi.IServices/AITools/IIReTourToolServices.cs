using AI_WebApi.Model.Domain.IReGo;
using AI_WebApi.Model.Domain.IReTour;
using AI_WebApi.Model.DTOs.AITools;

namespace AI_WebApi.IServices.AITools;

public interface IIReTourPatientContextService
{
    Task<ToolResponseDto<IReTourPatientContextDto>> GetAsync(
        TrustedPatientContext context,
        GetIReTourPatientContextRequestDto request,
        string requestId,
        CancellationToken cancellationToken);
}

public interface IIReTourPatientHistoryService
{
    Task<ToolResponseDto<IReTourPatientHistoryDto>> GetAsync(
        TrustedPatientContext context,
        GetIReTourPatientHistoryRequestDto request,
        string requestId,
        CancellationToken cancellationToken);
}

public interface IIReTourSessionAnalysisService
{
    Task<ToolResponseDto<IReTourSessionAnalysisDto>> GetAsync(
        TrustedPatientContext context,
        GetIReTourSessionAnalysisRequestDto request,
        string requestId,
        CancellationToken cancellationToken);
}

public interface IIReTourLongitudinalAnalysisService
{
    Task<ToolResponseDto<IReTourLongitudinalAnalysisDto>> GetAsync(
        TrustedPatientContext context,
        GetIReTourLongitudinalAnalysisRequestDto request,
        string requestId,
        CancellationToken cancellationToken);
}

/// <summary>Shared evaluation pipeline: binding, device, time, state and metrics.</summary>
public interface IIReTourPatientEvaluationService
{
    Task<IReTourEvaluationResult> EvaluateAsync(
        TrustedPatientContext context,
        string requestId,
        CancellationToken cancellationToken);
}

public interface IIReTourSessionRefProvider
{
    string Issue(
        IReTourEvaluatedSession session);

    /// <summary>
    /// Verifies and unwraps a stable session reference. Returns null when the
    /// signature is invalid; callers map null to a not-found response without
    /// leaking whether the session belongs to another patient.
    /// </summary>
    IReTourEvaluatedSession? Verify(
        string sessionRef,
        int patientId,
        IReadOnlyList<IReTourEvaluatedSession> candidates);
}
