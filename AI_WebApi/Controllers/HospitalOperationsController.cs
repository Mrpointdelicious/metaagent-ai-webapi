using AI_WebApi.IServices.AITools;
using AI_WebApi.Model.AITools;
using AI_WebApi.Model.DTOs.AITools;
using AI_WebApi.Security;
using Microsoft.AspNetCore.Mvc;

namespace AI_WebApi.Controllers;

[ApiController]
[Route("api/ai/hospital-operations")]
[ApiExplorerSettings(GroupName = "ai-hospital-tools-v1")]
[Produces("application/json")]
public sealed class HospitalOperationsController : ControllerBase
{
    private readonly IHospitalOperationsService _service;
    private readonly IHospitalOperationsArtifactStore _artifacts;
    private readonly IDifyServiceRequestAuthenticator _authenticator;

    public HospitalOperationsController(
        IHospitalOperationsService service,
        IHospitalOperationsArtifactStore artifacts,
        IDifyServiceRequestAuthenticator authenticator)
    {
        _service = service;
        _artifacts = artifacts;
        _authenticator = authenticator;
    }

    [HttpPost(
        "tools/query_hospital_operations",
        Name = "query_hospital_operations")]
    [ProducesResponseType(
        typeof(HospitalOperationsToolResponseDto),
        StatusCodes.Status200OK)]
    [ProducesResponseType(
        typeof(HospitalOperationsToolResponseDto),
        StatusCodes.Status400BadRequest)]
    [ProducesResponseType(
        typeof(HospitalOperationsToolResponseDto),
        StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<HospitalOperationsToolResponseDto>> Query(
        [FromBody] HospitalOperationsQueryRequestDto? request,
        CancellationToken cancellationToken)
    {
        var requestId = Request.Headers["X-Request-Id"].FirstOrDefault()?.Trim();
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
        if (request == null)
        {
            return BadRequest(Failure(
                requestId,
                HospitalOperationsReasonCodes.InvalidRequest));
        }

        var baseUrl = $"{Request.Scheme}://{Request.Host.Value}";
        var result = await _service.QueryAsync(
            request,
            baseUrl,
            requestId,
            cancellationToken);
        if (result.Status == "invalid_request")
        {
            return BadRequest(result);
        }
        return Ok(result);
    }

    [HttpGet("directory")]
    [ProducesResponseType(
        typeof(IReadOnlyList<HospitalDirectoryEntryDto>),
        StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<HospitalDirectoryEntryDto>>>
        Directory(CancellationToken cancellationToken)
    {
        var authenticated = _authenticator.Authenticate(HttpContext);
        if (!authenticated.IsSuccess)
        {
            return StatusCode(authenticated.HttpStatusCode);
        }
        return Ok(await _service.GetDirectoryAsync(cancellationToken));
    }

    [HttpGet("report-artifacts/{artifactRef}/content")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public IActionResult ReportArtifact(
        [FromRoute] string artifactRef,
        [FromQuery] string format = "png")
    {
        SetArtifactHeaders();
        var result = _artifacts.Resolve(
            artifactRef,
            format.Trim().ToLowerInvariant());
        return result.Status switch
        {
            HospitalReportAccessStatus.Available =>
                File(result.Bytes, result.MimeType),
            HospitalReportAccessStatus.Missing => NotFound(),
            _ => BadRequest()
        };
    }

    private void SetArtifactHeaders()
    {
        Response.Headers["Cache-Control"] =
            "private, max-age=31536000, immutable";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
    }

    private static HospitalOperationsToolResponseDto Failure(
        string requestId,
        string reasonCode) => new()
        {
            RequestId = requestId,
            Status = "failed",
            ReasonCodes = new[] { reasonCode },
            AgentSummary = "医院运营工具请求未通过校验。"
        };
}
