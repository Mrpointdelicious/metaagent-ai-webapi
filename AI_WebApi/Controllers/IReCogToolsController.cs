using AI_WebApi.IServices.AITools;
using AI_WebApi.Model.AITools;
using AI_WebApi.Model.DTOs.AITools;
using AI_WebApi.Repository.AITools;
using AI_WebApi.Security;
using Microsoft.AspNetCore.Mvc;

namespace AI_WebApi.Controllers;

[ApiController]
[Route("api/ai/patients/tools")]
[ApiExplorerSettings(GroupName = "ai-patient-tools-v2")]
public sealed class IReCogToolsController : ControllerBase
{
    private readonly IIReCogToolService _service;
    private readonly IDifySystemContextResolver _resolver;
    public IReCogToolsController(IIReCogToolService service, IDifySystemContextResolver resolver)
    { _service = service; _resolver = resolver; }

    [HttpPost("get_irecog_patient_context", Name = "get_irecog_patient_context")]
    [ProducesResponseType(typeof(ToolResponseDto<IReCogAnalysisDto>), StatusCodes.Status200OK)]
    public Task<IActionResult> Overview(IReCogRequestDto request, CancellationToken cancellationToken) => Execute("patient_context", request, false, cancellationToken);
    [HttpPost("get_irecog_patient_history", Name = "get_irecog_patient_history")]
    [ProducesResponseType(typeof(ToolResponseDto<IReCogAnalysisDto>), StatusCodes.Status200OK)]
    public Task<IActionResult> History(IReCogRequestDto request, CancellationToken cancellationToken) => Execute("patient_history", request, false, cancellationToken);
    [HttpPost("get_irecog_session_analysis", Name = "get_irecog_session_analysis")]
    [ProducesResponseType(typeof(ToolResponseDto<IReCogAnalysisDto>), StatusCodes.Status200OK)]
    public Task<IActionResult> Session(IReCogRequestDto request, CancellationToken cancellationToken) => Execute("session_analysis", request, false, cancellationToken);
    [HttpPost("get_irecog_longitudinal_analysis", Name = "get_irecog_longitudinal_analysis")]
    [ProducesResponseType(typeof(ToolResponseDto<IReCogAnalysisDto>), StatusCodes.Status200OK)]
    public Task<IActionResult> Trend(IReCogRequestDto request, CancellationToken cancellationToken) => Execute("longitudinal_analysis", request, false, cancellationToken);
    [HttpPost("generate_irecog_single_session_report", Name = "generate_irecog_single_session_report")]
    [ProducesResponseType(typeof(ToolResponseDto<IReTourSingleSessionReportArtifactDto>), StatusCodes.Status200OK)]
    public Task<IActionResult> SingleReport(IReCogRequestDto request, CancellationToken cancellationToken) => Execute("single_session_report", request, true, cancellationToken);
    [HttpPost("generate_irecog_longitudinal_report", Name = "generate_irecog_longitudinal_report")]
    [ProducesResponseType(typeof(ToolResponseDto<IReTourSingleSessionReportArtifactDto>), StatusCodes.Status200OK)]
    public Task<IActionResult> TrendReport(IReCogRequestDto request, CancellationToken cancellationToken) => Execute("longitudinal_report", request, true, cancellationToken);

    private async Task<IActionResult> Execute(string operation, IReCogRequestDto request, bool report, CancellationToken cancellationToken)
    {
        var tool = (report ? "generate_irecog_" : "get_irecog_") + operation;
        var id = HttpContext.TraceIdentifier;
        object Failure(string code) => ToolResponseDto<object>.Create(tool, id, ToolStatuses.Unavailable,
            null, new[] { code }, new ToolMetaDto(), contractVersion: IReCogToolOptions.ContractVersion);
        var trusted = _resolver.Resolve(HttpContext, request.SystemContext);
        if (!trusted.IsSuccess) return StatusCode(trusted.HttpStatusCode, Failure(trusted.ReasonCode!));
        try
        {
            if (report) return Ok(await _service.ReportAsync(operation == "longitudinal_report", trusted.Context!, request,
                $"{Request.Scheme}://{Request.Host}", id, cancellationToken));
            return Ok(await _service.ReadAsync(operation, trusted.Context!, request, id, cancellationToken));
        }
        catch (IReCogReadLimitException) { return Ok(Failure("IRECOG_READ_LIMIT_EXCEEDED")); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { return StatusCode(503, Failure("IRECOG_DEPENDENCY_UNAVAILABLE")); }
    }
}
