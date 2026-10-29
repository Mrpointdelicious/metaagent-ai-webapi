using AI_WebApi.IServices.AITools;
using AI_WebApi.Model.AITools;
using AI_WebApi.Model.DTOs.AITools;
using AI_WebApi.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace AI_WebApi.Controllers;

[ApiController]
[Route("api/ai/patients/tools")]
[ApiExplorerSettings(GroupName = "ai-patient-tools-v2")]
[Produces("application/json")]
public sealed class IReGoToolsController : ControllerBase
{
    private readonly IMultisourcePatientContextService _contextService;
    private readonly IIReGoSessionAnalysisService _sessionService;
    private readonly IIReGoPatientHistoryService _historyService;
    private readonly IIReGoSingleSessionReportService _reportService;
    private readonly IIReGoLongitudinalAnalysisService _longitudinalService;
    private readonly IIReGoLongitudinalReportService _longitudinalReportService;
    private readonly IDifySystemContextResolver _contextResolver;
    private readonly IReGoToolOptions _options;
    private readonly ILogger<IReGoToolsController> _logger;

    public IReGoToolsController(
        IMultisourcePatientContextService contextService,
        IIReGoSessionAnalysisService sessionService,
        IIReGoPatientHistoryService historyService,
        IIReGoSingleSessionReportService reportService,
        IIReGoLongitudinalAnalysisService longitudinalService,
        IIReGoLongitudinalReportService longitudinalReportService,
        IDifySystemContextResolver contextResolver,
        IOptions<IReGoToolOptions> options,
        ILogger<IReGoToolsController> logger)
    {
        _contextService = contextService;
        _sessionService = sessionService;
        _historyService = historyService;
        _reportService = reportService;
        _longitudinalService = longitudinalService;
        _longitudinalReportService = longitudinalReportService;
        _contextResolver = contextResolver;
        _options = options.Value;
        _logger = logger;
    }

    [HttpPost(
        "get_irego_patient_history",
        Name = "get_irego_patient_history")]
    [ProducesResponseType(
        typeof(ToolResponseDto<IReGoPatientHistoryDto>),
        StatusCodes.Status200OK)]
    [ProducesResponseType(
        typeof(ToolResponseDto<IReGoPatientHistoryDto>),
        StatusCodes.Status400BadRequest)]
    [ProducesResponseType(
        typeof(ToolResponseDto<IReGoPatientHistoryDto>),
        StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(
        typeof(ToolResponseDto<IReGoPatientHistoryDto>),
        StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ToolResponseDto<IReGoPatientHistoryDto>>>
        GetIReGoPatientHistory(
            [FromBody] GetIReGoPatientHistoryRequestDto? request,
            CancellationToken cancellationToken)
    {
        var requestId = RequestId();
        if (!ValidHistoryRequest(request))
        {
            return BadRequest(Error<IReGoPatientHistoryDto>(
                "get_irego_patient_history",
                requestId,
                IReGoReasonCodes.InvalidRequest));
        }

        var trusted = _contextResolver.Resolve(
            HttpContext,
            request!.SystemContext);
        if (!trusted.IsSuccess)
        {
            return StatusCode(
                trusted.HttpStatusCode,
                Error<IReGoPatientHistoryDto>(
                    "get_irego_patient_history",
                    requestId,
                    trusted.ReasonCode!));
        }

        try
        {
            var response = await _historyService.GetAsync(
                trusted.Context!,
                request,
                requestId,
                cancellationToken);
            return MapExpectedFailure(response);
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
                "get_irego_patient_history failed. RequestId={RequestId}",
                requestId);
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                Error<IReGoPatientHistoryDto>(
                    "get_irego_patient_history",
                    requestId,
                    "DEPENDENCY_FAILURE"));
        }
    }

    [HttpPost(
        "get_multisource_patient_context",
        Name = "get_multisource_patient_context")]
    [ProducesResponseType(
        typeof(ToolResponseDto<MultisourcePatientContextDto>),
        StatusCodes.Status200OK)]
    [ProducesResponseType(
        typeof(ToolResponseDto<MultisourcePatientContextDto>),
        StatusCodes.Status400BadRequest)]
    [ProducesResponseType(
        typeof(ToolResponseDto<MultisourcePatientContextDto>),
        StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(
        typeof(ToolResponseDto<MultisourcePatientContextDto>),
        StatusCodes.Status403Forbidden)]
    [ProducesResponseType(
        typeof(ToolResponseDto<MultisourcePatientContextDto>),
        StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<ToolResponseDto<MultisourcePatientContextDto>>>
        GetMultisourcePatientContext(
            [FromBody] GetMultisourcePatientContextRequestDto? request,
            CancellationToken cancellationToken)
    {
        var requestId = RequestId();
        if (request == null ||
            request.ProjectionLevel is not ("compact" or "standard"))
        {
            return BadRequest(Error<MultisourcePatientContextDto>(
                "get_multisource_patient_context",
                requestId,
                IReGoReasonCodes.InvalidRequest));
        }

        var trusted = _contextResolver.Resolve(
            HttpContext,
            request.SystemContext);
        if (!trusted.IsSuccess)
        {
            return StatusCode(
                trusted.HttpStatusCode,
                Error<MultisourcePatientContextDto>(
                    "get_multisource_patient_context",
                    requestId,
                    trusted.ReasonCode!));
        }

        try
        {
            var response = await _contextService.GetAsync(
                trusted.Context!,
                request,
                requestId,
                cancellationToken);
            return MapExpectedFailure(response);
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
                "get_multisource_patient_context failed. RequestId={RequestId}",
                requestId);
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                Error<MultisourcePatientContextDto>(
                    "get_multisource_patient_context",
                    requestId,
                    "DEPENDENCY_FAILURE"));
        }
    }

    [HttpPost(
        "get_irego_session_analysis",
        Name = "get_irego_session_analysis")]
    [ProducesResponseType(
        typeof(ToolResponseDto<IReGoSessionAnalysisDto>),
        StatusCodes.Status200OK)]
    [ProducesResponseType(
        typeof(ToolResponseDto<IReGoSessionAnalysisDto>),
        StatusCodes.Status400BadRequest)]
    [ProducesResponseType(
        typeof(ToolResponseDto<IReGoSessionAnalysisDto>),
        StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(
        typeof(ToolResponseDto<IReGoSessionAnalysisDto>),
        StatusCodes.Status403Forbidden)]
    [ProducesResponseType(
        typeof(ToolResponseDto<IReGoSessionAnalysisDto>),
        StatusCodes.Status404NotFound)]
    [ProducesResponseType(
        typeof(ToolResponseDto<IReGoSessionAnalysisDto>),
        StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<ToolResponseDto<IReGoSessionAnalysisDto>>>
        GetIReGoSessionAnalysis(
            [FromBody] GetIReGoSessionAnalysisRequestDto? request,
            CancellationToken cancellationToken)
    {
        var requestId = RequestId();
        if (!ValidSessionRequest(request))
        {
            return BadRequest(Error<IReGoSessionAnalysisDto>(
                "get_irego_session_analysis",
                requestId,
                IReGoReasonCodes.InvalidRequest));
        }

        var trusted = _contextResolver.Resolve(
            HttpContext,
            request!.SystemContext);
        if (!trusted.IsSuccess)
        {
            return StatusCode(
                trusted.HttpStatusCode,
                Error<IReGoSessionAnalysisDto>(
                    "get_irego_session_analysis",
                    requestId,
                    trusted.ReasonCode!));
        }

        try
        {
            var response = await _sessionService.GetAsync(
                trusted.Context!,
                request!,
                requestId,
                cancellationToken);
            return MapExpectedFailure(response);
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
                "get_irego_session_analysis failed. RequestId={RequestId}",
                requestId);
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                Error<IReGoSessionAnalysisDto>(
                    "get_irego_session_analysis",
                    requestId,
                    "DEPENDENCY_FAILURE"));
        }
    }

    [HttpPost(
        "generate_irego_single_session_report",
        Name = "generate_irego_single_session_report")]
    [ProducesResponseType(
        typeof(ToolResponseDto<IReGoSingleSessionReportArtifactDto>),
        StatusCodes.Status200OK)]
    [ProducesResponseType(
        typeof(ToolResponseDto<IReGoSingleSessionReportArtifactDto>),
        StatusCodes.Status400BadRequest)]
    [ProducesResponseType(
        typeof(ToolResponseDto<IReGoSingleSessionReportArtifactDto>),
        StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(
        typeof(ToolResponseDto<IReGoSingleSessionReportArtifactDto>),
        StatusCodes.Status404NotFound)]
    [ProducesResponseType(
        typeof(ToolResponseDto<IReGoSingleSessionReportArtifactDto>),
        StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<ToolResponseDto<IReGoSingleSessionReportArtifactDto>>>
        GenerateIReGoSingleSessionReport(
            [FromBody] GenerateIReGoSingleSessionReportRequestDto? request,
            CancellationToken cancellationToken)
    {
        var requestId = RequestId();
        if (request == null ||
            string.IsNullOrWhiteSpace(request.SessionRef) ||
            request.ForbiddenAiEvaluation != null ||
            request.ForbiddenReportType != null)
        {
            return BadRequest(Error<IReGoSingleSessionReportArtifactDto>(
                "generate_irego_single_session_report",
                requestId,
                IReGoReasonCodes.InvalidRequest));
        }

        var trusted = _contextResolver.Resolve(HttpContext, request.SystemContext);
        if (!trusted.IsSuccess)
        {
            return StatusCode(
                trusted.HttpStatusCode,
                Error<IReGoSingleSessionReportArtifactDto>(
                    "generate_irego_single_session_report",
                    requestId,
                    trusted.ReasonCode!));
        }

        try
        {
            var requestBaseUrl = $"{Request.Scheme}://{Request.Host.Value}";
            var response = await _reportService.GenerateAsync(
                trusted.Context!,
                request,
                requestBaseUrl,
                requestId,
                cancellationToken);
            return MapExpectedFailure(response);
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
                "generate_irego_single_session_report failed. RequestId={RequestId}",
                requestId);
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                Error<IReGoSingleSessionReportArtifactDto>(
                    "generate_irego_single_session_report",
                    requestId,
                    "DEPENDENCY_FAILURE"));
        }
    }

    [HttpPost(
        "get_irego_longitudinal_analysis",
        Name = "get_irego_longitudinal_analysis")]
    [ProducesResponseType(
        typeof(ToolResponseDto<IReGoLongitudinalAnalysisDto>),
        StatusCodes.Status200OK)]
    [ProducesResponseType(
        typeof(ToolResponseDto<IReGoLongitudinalAnalysisDto>),
        StatusCodes.Status400BadRequest)]
    [ProducesResponseType(
        typeof(ToolResponseDto<IReGoLongitudinalAnalysisDto>),
        StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(
        typeof(ToolResponseDto<IReGoLongitudinalAnalysisDto>),
        StatusCodes.Status404NotFound)]
    [ProducesResponseType(
        typeof(ToolResponseDto<IReGoLongitudinalAnalysisDto>),
        StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<ToolResponseDto<IReGoLongitudinalAnalysisDto>>>
        GetIReGoLongitudinalAnalysis(
            [FromBody] GetIReGoLongitudinalAnalysisRequestDto? request,
            CancellationToken cancellationToken)
    {
        var requestId = RequestId();
        if (!ValidLongitudinalRequest(request))
        {
            return BadRequest(Error<IReGoLongitudinalAnalysisDto>(
                "get_irego_longitudinal_analysis",
                requestId,
                IReGoReasonCodes.InvalidRequest));
        }

        var trusted = _contextResolver.Resolve(HttpContext, request!.SystemContext);
        if (!trusted.IsSuccess)
        {
            return StatusCode(
                trusted.HttpStatusCode,
                Error<IReGoLongitudinalAnalysisDto>(
                    "get_irego_longitudinal_analysis",
                    requestId,
                    trusted.ReasonCode!));
        }

        try
        {
            var response = await _longitudinalService.GetAsync(
                trusted.Context!,
                request,
                requestId,
                cancellationToken);
            return MapExpectedFailure(response);
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
                "get_irego_longitudinal_analysis failed. RequestId={RequestId}",
                requestId);
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                Error<IReGoLongitudinalAnalysisDto>(
                    "get_irego_longitudinal_analysis",
                    requestId,
                    "DEPENDENCY_FAILURE"));
        }
    }

    [HttpPost(
        "generate_irego_longitudinal_report",
        Name = "generate_irego_longitudinal_report")]
    [ProducesResponseType(
        typeof(ToolResponseDto<IReGoLongitudinalReportArtifactDto>),
        StatusCodes.Status200OK)]
    [ProducesResponseType(
        typeof(ToolResponseDto<IReGoLongitudinalReportArtifactDto>),
        StatusCodes.Status400BadRequest)]
    [ProducesResponseType(
        typeof(ToolResponseDto<IReGoLongitudinalReportArtifactDto>),
        StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(
        typeof(ToolResponseDto<IReGoLongitudinalReportArtifactDto>),
        StatusCodes.Status404NotFound)]
    [ProducesResponseType(
        typeof(ToolResponseDto<IReGoLongitudinalReportArtifactDto>),
        StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<ToolResponseDto<IReGoLongitudinalReportArtifactDto>>>
        GenerateIReGoLongitudinalReport(
            [FromBody] GenerateIReGoLongitudinalReportRequestDto? request,
            CancellationToken cancellationToken)
    {
        var requestId = RequestId();
        if (!ValidLongitudinalReportRequest(request))
        {
            return BadRequest(Error<IReGoLongitudinalReportArtifactDto>(
                "generate_irego_longitudinal_report",
                requestId,
                IReGoReasonCodes.InvalidRequest));
        }

        var trusted = _contextResolver.Resolve(HttpContext, request!.SystemContext);
        if (!trusted.IsSuccess)
        {
            return StatusCode(
                trusted.HttpStatusCode,
                Error<IReGoLongitudinalReportArtifactDto>(
                    "generate_irego_longitudinal_report",
                    requestId,
                    trusted.ReasonCode!));
        }

        try
        {
            var requestBaseUrl = $"{Request.Scheme}://{Request.Host.Value}";
            var response = await _longitudinalReportService.GenerateAsync(
                trusted.Context!,
                request,
                requestBaseUrl,
                requestId,
                cancellationToken);
            return MapExpectedFailure(response);
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
                "generate_irego_longitudinal_report failed. RequestId={RequestId}",
                requestId);
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                Error<IReGoLongitudinalReportArtifactDto>(
                    "generate_irego_longitudinal_report",
                    requestId,
                    "DEPENDENCY_FAILURE"));
        }
    }

    [HttpGet("/api/ai/patients/report-images/{token}")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public IActionResult GetIReGoReportImage([FromRoute] string token)
    {
        Response.Headers["Cache-Control"] = "private, no-store, max-age=0";
        Response.Headers["Pragma"] = "no-cache";
        Response.Headers["X-Content-Type-Options"] = "nosniff";

        var result = _reportService.ResolveImage(token);
        if (result.ArtifactRef.StartsWith("irego_scope_v2_", StringComparison.Ordinal)) return NotFound();
        if (result.Status == ReportImageAccessStatus.Expired &&
            !string.IsNullOrWhiteSpace(result.ArtifactRef))
        {
            var stable = _reportService.ResolveArtifact(result.ArtifactRef);
            if (stable.Status == ReportImageAccessStatus.Available)
            {
                return Redirect(
                    $"/api/ai/patients/report-artifacts/" +
                    $"{Uri.EscapeDataString(result.ArtifactRef)}/content");
            }
        }

        return result.Status switch
        {
            ReportImageAccessStatus.Available when result.Bytes != null =>
                File(result.Bytes, result.MimeType),
            ReportImageAccessStatus.Expired =>
                StatusCode(StatusCodes.Status410Gone),
            _ => NotFound()
        };
    }

    [HttpGet("/api/ai/patients/report-artifacts/{artifactRef}/content")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public IActionResult GetIReGoReportArtifact(
        [FromRoute] string artifactRef)
    {
        if (artifactRef.StartsWith("irego_scope_v2_", StringComparison.Ordinal)) return NotFound();
        Response.Headers["Cache-Control"] = "private, no-cache, max-age=0";
        Response.Headers["X-Content-Type-Options"] = "nosniff";

        var result = _reportService.ResolveArtifact(artifactRef);
        return result.Status switch
        {
            ReportImageAccessStatus.Available when result.Bytes != null =>
                File(result.Bytes, result.MimeType),
            _ => NotFound()
        };
    }

    private ActionResult<ToolResponseDto<T>> MapExpectedFailure<T>(
        ToolResponseDto<T> response)
    {
        if (response.Status != ToolStatuses.Failed)
        {
            return Ok(response);
        }

        if (response.ReasonCodes.Any(x =>
                x is IReGoReasonCodes.PatientNotFound or
                    IReGoReasonCodes.SessionRefNotFound or
                    IReGoReasonCodes.SessionRefContextMismatch))
        {
            return NotFound(response);
        }

        if (response.ReasonCodes.Any(x =>
                x is IReGoReasonCodes.InvalidRequest or
                    IReGoReasonCodes.LongitudinalWindowTooLarge or
                    IReGoReasonCodes.SingleReportAiEvaluationRequired or
                    IReGoReasonCodes.SingleReportAiEvaluationInvalid or
                    IReGoReasonCodes.SingleReportAiEvaluationMismatch))
        {
            return BadRequest(response);
        }

        if (response.ReasonCodes.Contains(
                IReGoReasonCodes.SessionRefSigningKeyNotConfigured) ||
            response.ReasonCodes.Contains(
                IReGoReasonCodes.ReportImageSigningKeyNotConfigured) ||
            response.ReasonCodes.Contains(
                IReGoReasonCodes.ReportRenderingUnavailable))
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, response);
        }

        return StatusCode(StatusCodes.Status500InternalServerError, response);
    }

    private bool ValidSessionRequest(GetIReGoSessionAnalysisRequestDto? request)
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

    private static bool ValidLongitudinalRequest(
        GetIReGoLongitudinalAnalysisRequestDto? request)
    {
        if (request == null ||
            request.Selector != "latest_contiguous" ||
            request.ForbiddenSessionRefsJson != null ||
            (request.SessionRefs?.Count ?? 0) != 0 ||
            request.SelectionMode is not (
                "latest_count" or "recent_ordinal_range" or "date_range") ||
            request.ProjectScope is not
                ("all" or "walking" or "sit_stand" or "games" or "balance") ||
            request.DetailLevel is not ("summary" or "standard") ||
            request.QualityDetail is not ("none" or "summary" or "full"))
        {
            return false;
        }

        return ValidSemanticWindow(
            request.SelectionMode,
            request.ReportCount,
            request.StartOrdinal,
            request.EndOrdinal,
            request.StartDate,
            request.EndDate);
    }

    private static bool ValidHistoryRequest(
        GetIReGoPatientHistoryRequestDto? request) =>
        request != null &&
        request.PageNumber >= 1 &&
        request.PageSize is >= 1 and <= 50 &&
        request.RecordScope is "all" or "reports" or "plans" &&
        request.ProjectScope is
            "all" or "walking" or "sit_stand" or "games" or "balance" &&
        request.TrainingState is
            "all" or "completed" or "not_started" or "not_completed" or
            "execution_unknown" or "voided";

    private static bool ValidLongitudinalReportRequest(
        GenerateIReGoLongitudinalReportRequestDto? request)
    {
        if (request == null ||
            request.Selector != "latest_contiguous" ||
            request.ForbiddenSessionRefsJson != null ||
            request.ForbiddenReportType != null ||
            (request.SessionRefs?.Count ?? 0) != 0 ||
            request.SelectionMode is not (
                "latest_count" or "recent_ordinal_range" or "date_range") ||
            request.ProjectScope is not
                ("all" or "walking" or "sit_stand" or "games" or "balance"))
        {
            return false;
        }

        return ValidSemanticWindow(
            request.SelectionMode,
            request.ReportCount,
            request.StartOrdinal,
            request.EndOrdinal,
            request.StartDate,
            request.EndDate);
    }

    private static bool ValidSemanticWindow(
        string mode,
        int reportCount,
        int? startOrdinal,
        int? endOrdinal,
        DateTimeOffset? startDate,
        DateTimeOffset? endDate) => mode switch
        {
            "latest_count" =>
                reportCount is >= 2 and <= 10 &&
                startOrdinal == null && endOrdinal == null &&
                startDate == null && endDate == null,
            "recent_ordinal_range" =>
                startOrdinal is >= 1 && endOrdinal is >= 1 &&
                Math.Abs(startOrdinal.Value - endOrdinal.Value) + 1 is
                    >= 2 and <= 10 &&
                startDate == null && endDate == null,
            "date_range" =>
                startOrdinal == null && endOrdinal == null &&
                startDate.HasValue && endDate.HasValue &&
                startDate.Value <= endDate.Value,
            _ => false
        };

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
                SourceCompleteness = _options.SourceCompleteness
            });

    private string RequestId() =>
        Request.Headers["X-Request-Id"].FirstOrDefault() ??
        HttpContext.TraceIdentifier;
}
