using AI_WebApi.IServices.AITools;
using AI_WebApi.Model.AITools;
using AI_WebApi.Model.DTOs.AITools;
using AI_WebApi.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace AI_WebApi.Controllers;

[ApiController]
[Route("api/ai/patients/tools")]
[ApiExplorerSettings(GroupName = "ai-patient-tools-v1")]
[Produces("application/json")]
public sealed class PatientIdentityMappingToolsController : ControllerBase
{
    private const string ToolName = "resolve_patient_identity";
    private readonly IPatientIdentityMappingService _service;
    private readonly IDifyServiceRequestAuthenticator _authenticator;
    private readonly IReGoToolOptions _options;
    private readonly ILogger<PatientIdentityMappingToolsController> _logger;

    public PatientIdentityMappingToolsController(
        IPatientIdentityMappingService service,
        IDifyServiceRequestAuthenticator authenticator,
        IOptions<IReGoToolOptions> options,
        ILogger<PatientIdentityMappingToolsController> logger)
    {
        _service = service;
        _authenticator = authenticator;
        _options = options.Value;
        _logger = logger;
    }

    [HttpPost(
        "resolve_patient_identity",
        Name = "resolve_patient_identity")]
    [ProducesResponseType(
        typeof(ToolResponseDto<PatientIdentityResolutionDto>),
        StatusCodes.Status200OK)]
    [ProducesResponseType(
        typeof(ToolResponseDto<PatientIdentityResolutionDto>),
        StatusCodes.Status400BadRequest)]
    [ProducesResponseType(
        typeof(ToolResponseDto<PatientIdentityResolutionDto>),
        StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(
        typeof(ToolResponseDto<PatientIdentityResolutionDto>),
        StatusCodes.Status404NotFound)]
    [ProducesResponseType(
        typeof(ToolResponseDto<PatientIdentityResolutionDto>),
        StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<ToolResponseDto<PatientIdentityResolutionDto>>>
        ResolvePatientIdentity(
            [FromBody] ResolvePatientIdentityRequestDto? request,
            CancellationToken cancellationToken)
    {
        var requestId = Request.Headers["X-Request-Id"]
            .FirstOrDefault()?.Trim();
        if (string.IsNullOrWhiteSpace(requestId))
        {
            requestId = HttpContext.TraceIdentifier;
        }

        var authenticated = _authenticator.Authenticate(HttpContext);
        if (!authenticated.IsSuccess)
        {
            return StatusCode(
                authenticated.HttpStatusCode,
                Failure(requestId, authenticated.ReasonCode!));
        }

        var phone = NormalizePhone(request?.Phone);
        var hasProjectPatientId = request?.ProjectPatientId > 0;
        if (request == null ||
            (!hasProjectPatientId && phone == null) ||
            (hasProjectPatientId && phone != null))
        {
            return BadRequest(Failure(
                requestId,
                IReGoReasonCodes.InvalidRequest));
        }

        try
        {
            var response = phone != null
                ? await _service.ResolveByPhoneAsync(
                    phone,
                    requestId,
                    cancellationToken)
                : await _service.ResolveAsync(
                    request.ProjectPatientId,
                    requestId,
                    cancellationToken);
            return response.Status == ToolStatuses.Success
                ? Ok(response)
                : NotFound(response);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "resolve_patient_identity failed. RequestId={RequestId}, " +
                "InputKind={InputKind}, ProjectPatientId={ProjectPatientId}",
                requestId,
                phone != null ? "phone" : "project_patient_id",
                request.ProjectPatientId);
            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                Failure(requestId, "DEPENDENCY_FAILURE"));
        }
    }

    private static string? NormalizePhone(string? value)
    {
        var phone = value?.Trim();
        if (string.IsNullOrWhiteSpace(phone) ||
            phone.Length is < 7 or > 20 ||
            phone.Any(character => character is < '0' or > '9'))
        {
            return null;
        }

        return phone;
    }

    private ToolResponseDto<PatientIdentityResolutionDto> Failure(
        string requestId,
        string reasonCode) =>
        ToolResponseDto<PatientIdentityResolutionDto>.Create(
            ToolName,
            requestId,
            ToolStatuses.Failed,
            null,
            new[] { reasonCode },
            new ToolMetaDto
            {
                SourceCompleteness = _options.SourceCompleteness
            });
}
