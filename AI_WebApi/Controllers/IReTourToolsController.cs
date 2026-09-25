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
public sealed class IReTourToolsController : ControllerBase
{
    private readonly IIReTourPatientContextService _contextService;
    private readonly IIReTourPatientHistoryService _historyService;
    private readonly IIReTourSessionAnalysisService _sessionService;
    private readonly IIReTourLongitudinalAnalysisService _longitudinalService;
    private readonly IDifySystemContextResolver _contextResolver;
    private readonly IReTourToolOptions _options;
    private readonly ILogger<IReTourToolsController> _logger;

    public IReTourToolsController(
        IIReTourPatientContextService contextService,
        IIReTourPatientHistoryService historyService,
        IIReTourSessionAnalysisService sessionService,
        IIReTourLongitudinalAnalysisService longitudinalService,
        IDifySystemContextResolver contextResolver,
        IOptions<IReTourToolOptions> options,
        ILogger<IReTourToolsController> logger)
    {
        _contextService = contextService;
        _historyService = historyService;
        _sessionService = sessionService;
        _longitudinalService = longitudinalService;
        _contextResolver = contextResolver;
        _options = options.Value;
        _logger = logger;
    }

    [HttpPost(
        "get_iretour_patient_context",
        Name = "get_iretour_patient_context")]
    [ProducesResponseType(
        typeof(ToolResponseDto<IReTourPatientContextDto>),
        StatusCodes.Status200OK)]
    [ProducesResponseType(
        typeof(ToolResponseDto<IReTourPatientContextDto>),
        StatusCodes.Status400BadRequest)]
    [ProducesResponseType(
        typeof(ToolResponseDto<IReTourPatientContextDto>),
        StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ToolResponseDto<IReTourPatientContextDto>>>
        GetIReTourPatientContext(
            [FromBody] GetIReTourPatientContextRequestDto? request,
            CancellationToken cancellationToken)
    {
        var requestId = RequestId();
        if (request == null || request.ProjectionLevel is not ("compact" or "standard"))
        {
            return BadRequest(Error<IReTourPatientContextDto>(
                "get_iretour_patient_context",
                requestId,
                IReTourReasonCodes.InvalidRequest));
        }

        return await Execute(
            "get_iretour_patient_context",
            requestId,
            request.SystemContext,
            cancellationToken,
            context => _contextService.GetAsync(
                context,
                request,
                requestId,
                cancellationToken),
            response => response);
    }

    [HttpPost(
        "get_iretour_patient_history",
        Name = "get_iretour_patient_history")]
    [ProducesResponseType(
        typeof(ToolResponseDto<IReTourPatientHistoryDto>),
        StatusCodes.Status200OK)]
    [ProducesResponseType(
        typeof(ToolResponseDto<IReTourPatientHistoryDto>),
        StatusCodes.Status400BadRequest)]
    [ProducesResponseType(
        typeof(ToolResponseDto<IReTourPatientHistoryDto>),
        StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(
        typeof(ToolResponseDto<IReTourPatientHistoryDto>),
        StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ToolResponseDto<IReTourPatientHistoryDto>>>
        GetIReTourPatientHistory(
            [FromBody] GetIReTourPatientHistoryRequestDto? request,
            CancellationToken cancellationToken)
    {
        var requestId = RequestId();
        if (!ValidHistoryRequest(request))
        {
            return BadRequest(Error<IReTourPatientHistoryDto>(
                "get_iretour_patient_history",
                requestId,
                IReTourReasonCodes.InvalidRequest));
        }

        return await Execute(
            "get_iretour_patient_history",
            requestId,
            request!.SystemContext,
            cancellationToken,
            context => _historyService.GetAsync(
                context,
                request,
                requestId,
                cancellationToken),
            response => response);
    }

    [HttpPost(
        "get_iretour_session_analysis",
        Name = "get_iretour_session_analysis")]
    [ProducesResponseType(
        typeof(ToolResponseDto<IReTourSessionAnalysisDto>),
        StatusCodes.Status200OK)]
    [ProducesResponseType(
        typeof(ToolResponseDto<IReTourSessionAnalysisDto>),
        StatusCodes.Status400BadRequest)]
    [ProducesResponseType(
        typeof(ToolResponseDto<IReTourSessionAnalysisDto>),
        StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(
        typeof(ToolResponseDto<IReTourSessionAnalysisDto>),
        StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ToolResponseDto<IReTourSessionAnalysisDto>>>
        GetIReTourSessionAnalysis(
            [FromBody] GetIReTourSessionAnalysisRequestDto? request,
            CancellationToken cancellationToken)
    {
        var requestId = RequestId();
        if (!ValidSessionRequest(request))
        {
            return BadRequest(Error<IReTourSessionAnalysisDto>(
                "get_iretour_session_analysis",
                requestId,
                IReTourReasonCodes.InvalidRequest));
        }

        return await Execute(
            "get_iretour_session_analysis",
            requestId,
            request!.SystemContext,
            cancellationToken,
            context => _sessionService.GetAsync(
                context,
                request,
                requestId,
                cancellationToken),
            response => response);
    }

    [HttpPost(
        "get_iretour_longitudinal_analysis",
        Name = "get_iretour_longitudinal_analysis")]
    [ProducesResponseType(
        typeof(ToolResponseDto<IReTourLongitudinalAnalysisDto>),
        StatusCodes.Status200OK)]
    [ProducesResponseType(
        typeof(ToolResponseDto<IReTourLongitudinalAnalysisDto>),
        StatusCodes.Status400BadRequest)]
    [ProducesResponseType(
        typeof(ToolResponseDto<IReTourLongitudinalAnalysisDto>),
        StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(
        typeof(ToolResponseDto<IReTourLongitudinalAnalysisDto>),
        StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ToolResponseDto<IReTourLongitudinalAnalysisDto>>>
        GetIReTourLongitudinalAnalysis(
            [FromBody] GetIReTourLongitudinalAnalysisRequestDto? request,
            CancellationToken cancellationToken)
    {
        var requestId = RequestId();
        if (!ValidLongitudinalRequest(request))
        {
            return BadRequest(Error<IReTourLongitudinalAnalysisDto>(
                "get_iretour_longitudinal_analysis",
                requestId,
                IReTourReasonCodes.InvalidRequest));
        }

        return await Execute(
            "get_iretour_longitudinal_analysis",
            requestId,
            request!.SystemContext,
            cancellationToken,
            context => _longitudinalService.GetAsync(
                context,
                request,
                requestId,
                cancellationToken),
            response => response);
    }

    private async Task<ActionResult<ToolResponseDto<T>>> Execute<T>(
        string toolName,
        string requestId,
        DifySystemContextDto? systemContext,
        CancellationToken cancellationToken,
        Func<Model.Domain.IReGo.TrustedPatientContext, Task<ToolResponseDto<T>>> run,
        Func<ToolResponseDto<T>, ToolResponseDto<T>> map)
    {
        var trusted = _contextResolver.Resolve(HttpContext, systemContext);
        if (!trusted.IsSuccess)
        {
            return StatusCode(
                trusted.HttpStatusCode,
                Error<T>(toolName, requestId, trusted.ReasonCode!));
        }

        try
        {
            var response = await run(trusted.Context!);
            if (response.Status != ToolStatuses.Failed)
            {
                return Ok(response);
            }

            if (response.ReasonCodes.Contains(IReTourReasonCodes.SessionRefNotFound))
            {
                return NotFound(response);
            }

            if (response.ReasonCodes.Contains(IReTourReasonCodes.InvalidRequest))
            {
                return BadRequest(response);
            }

            if (response.ReasonCodes.Contains(
                    IReTourReasonCodes.SessionRefSigningKeyNotConfigured))
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable, response);
            }

            return StatusCode(StatusCodes.Status500InternalServerError, response);
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
                "{ToolName} failed. RequestId={RequestId}",
                toolName,
                requestId);
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                Error<T>(toolName, requestId, IReTourReasonCodes.DependencyFailure));
        }
    }

    private static bool ValidSessionRequest(
        GetIReTourSessionAnalysisRequestDto? request)
    {
        if (request == null ||
            request.Selector is not ("latest_usable" or "session_ref") ||
            request.DetailLevel is not ("brief" or "summary" or "standard") ||
            request.QualityDetail is not ("none" or "summary" or "full"))
        {
            return false;
        }

        return request.Selector != "session_ref" ||
            !string.IsNullOrWhiteSpace(request.SessionRef);
    }

    private static bool ValidHistoryRequest(
        GetIReTourPatientHistoryRequestDto? request) =>
        request != null &&
        request.PageNumber >= 1 &&
        request.PageSize is >= 1 and <= 50 &&
        request.RecordScope is "all" or "with_result" or "without_result" &&
        request.ActivityScope is
            "all" or "straight_primary" or "straight_secondary" or
            "reaction_primary" or "reaction_secondary" or "balance_bridge" or
            "vibration" or "transverse" or "stride" or "sideway" or
            "gait_assessment" &&
        request.TrainingState is
            "all" or "completed" or "not_started" or "not_completed" or
            "interrupted" or "execution_unknown" &&
        request.ResultState is
            "all" or "summary_available" or "raw_only" or "no_result" or
            "result_conflicting" or "result_unusable";

    private static bool ValidLongitudinalRequest(
        GetIReTourLongitudinalAnalysisRequestDto? request)
    {
        if (request == null ||
            request.ActivityScope is not (
                "straight_primary" or "straight_secondary" or
                "reaction_primary" or "reaction_secondary" or "balance_bridge" or
                "vibration" or "transverse" or "stride" or "sideway" or
                "gait_assessment") ||
            request.SelectionMode is not (
                "latest_count" or "recent_ordinal_range" or "date_range") ||
            request.DetailLevel is not ("summary" or "standard") ||
            request.QualityDetail is not ("none" or "summary" or "full"))
        {
            return false;
        }

        return request.SelectionMode switch
        {
            "latest_count" =>
                request.ReportCount is >= 2 and <= 20 &&
                request.StartOrdinal == null && request.EndOrdinal == null &&
                request.StartDate == null && request.EndDate == null,
            "recent_ordinal_range" =>
                request.StartOrdinal is >= 1 && request.EndOrdinal is >= 1 &&
                Math.Abs(request.StartOrdinal.Value - request.EndOrdinal.Value) + 1 <= 20 &&
                request.StartDate == null && request.EndDate == null,
            "date_range" =>
                request.StartOrdinal == null && request.EndOrdinal == null &&
                request.StartDate.HasValue && request.EndDate.HasValue &&
                request.StartDate.Value <= request.EndDate.Value,
            _ => false
        };
    }

    private ToolResponseDto<T> Error<T>(
        string toolName,
        string requestId,
        string reasonCode) =>
        ToolResponseDto<T>.Create(
            toolName,
            requestId,
            ToolStatuses.Failed,
            default,
            new[] { reasonCode },
            new ToolMetaDto
            {
                SourceCompleteness = _options.SourceCompleteness,
                RegistryVersions = new RegistryVersionsDto
                {
                    Contract = IReTourContract.Version,
                    Device = IReTourContract.DeviceRegistryVersion,
                    Schema = IReTourContract.SchemaRegistryVersion,
                    Metric = IReTourContract.MetricRegistryVersion,
                    State = IReTourContract.StateRegistryVersion
                }
            },
            contractVersion: IReTourContract.Version);

    private string RequestId() =>
        Request.Headers["X-Request-Id"].FirstOrDefault() ??
        HttpContext.TraceIdentifier;
}
