using AI_WebApi.IServices.AITools;
using AI_WebApi.Model.AITools;
using AI_WebApi.Model.DTOs.AITools;
using AI_WebApi.Repository.AITools;
using Microsoft.Extensions.Options;

namespace AI_WebApi.Services.AITools.IReGo;

/// <summary>
/// Temporary identity bridge. A mapping is returned only when exact name plus
/// patient user type produces exactly one robotdb user. Zero and multiple
/// matches intentionally share PATIENT_BINDING_NOT_FOUND so callers cannot
/// choose or guess a candidate.
/// </summary>
public sealed class PatientIdentityMappingService
    : IPatientIdentityMappingService
{
    private const string ToolName = "resolve_patient_identity";
    private readonly IPatientIdentityMappingRepository _repository;
    private readonly IReGoToolOptions _options;

    public PatientIdentityMappingService(
        IPatientIdentityMappingRepository repository,
        IOptions<IReGoToolOptions> options)
    {
        _repository = repository;
        _options = options.Value;
    }

    public async Task<ToolResponseDto<PatientIdentityResolutionDto>> ResolveAsync(
        int projectPatientId,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        var candidates = await _repository.FindRobotPatientCandidatesAsync(
            projectPatientId,
            cancellationToken);
        var uniqueCandidates = candidates.Distinct().Take(2).ToArray();
        if (uniqueCandidates.Length != 1)
        {
            return ToolResponseDto<PatientIdentityResolutionDto>.Create(
                ToolName,
                requestId,
                ToolStatuses.Failed,
                null,
                new[] { IReGoReasonCodes.PatientBindingNotFound },
                Meta(),
                "没有找到唯一对应的患者，请核对患者信息。");
        }

        return ToolResponseDto<PatientIdentityResolutionDto>.Create(
            ToolName,
            requestId,
            ToolStatuses.Success,
            new PatientIdentityResolutionDto
            {
                ProjectPatientId = projectPatientId,
                RobotPatientId = uniqueCandidates[0]
            },
            Array.Empty<string>(),
            Meta());
    }

    public async Task<ToolResponseDto<PatientIdentityResolutionDto>>
        ResolveByPhoneAsync(
            string phone,
            string requestId,
            CancellationToken cancellationToken = default)
    {
        var candidates = await _repository
            .FindRobotPatientCandidatesByPhoneAsync(
                phone,
                cancellationToken);
        var uniqueCandidates = candidates
            .Distinct()
            .Take(2)
            .ToArray();
        if (uniqueCandidates.Length != 1)
        {
            return BindingNotFound(requestId);
        }

        var candidate = uniqueCandidates[0];
        return ToolResponseDto<PatientIdentityResolutionDto>.Create(
            ToolName,
            requestId,
            ToolStatuses.Success,
            new PatientIdentityResolutionDto
            {
                ProjectPatientId = candidate.ProjectPatientId,
                RobotPatientId = candidate.RobotPatientId
            },
            Array.Empty<string>(),
            Meta());
    }

    private ToolResponseDto<PatientIdentityResolutionDto> BindingNotFound(
        string requestId) =>
        ToolResponseDto<PatientIdentityResolutionDto>.Create(
            ToolName,
            requestId,
            ToolStatuses.Failed,
            null,
            new[] { IReGoReasonCodes.PatientBindingNotFound },
            Meta(),
            "没有找到唯一对应的患者，请核对患者信息。");

    private ToolMetaDto Meta() => new()
    {
        SourceCompleteness = _options.SourceCompleteness
    };
}
