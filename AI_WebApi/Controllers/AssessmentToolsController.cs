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
[Produces("application/json")]
public sealed class AssessmentToolsController : ControllerBase
{
    private readonly IAssessmentToolService _service;
    private readonly IDifySystemContextResolver _resolver;
    public AssessmentToolsController(IAssessmentToolService service, IDifySystemContextResolver resolver)
    { _service = service; _resolver = resolver; }

    [HttpPost("get_assessment_patient_context", Name = "get_assessment_patient_context")]
    [ProducesResponseType(typeof(ToolResponseDto<AssessmentToolDataDto>), StatusCodes.Status200OK)]
    public Task<IActionResult> Overview(AssessmentToolRequestDto request, CancellationToken cancellationToken) => Execute("patient_context", request, false, cancellationToken);
    [HttpPost("get_assessment_patient_history", Name = "get_assessment_patient_history")]
    [ProducesResponseType(typeof(ToolResponseDto<AssessmentToolDataDto>), StatusCodes.Status200OK)]
    public Task<IActionResult> History(AssessmentToolRequestDto request, CancellationToken cancellationToken) => Execute("patient_history", request, false, cancellationToken);
    [HttpPost("get_assessment_result_analysis", Name = "get_assessment_result_analysis")]
    [ProducesResponseType(typeof(ToolResponseDto<AssessmentToolDataDto>), StatusCodes.Status200OK)]
    public Task<IActionResult> Single(AssessmentToolRequestDto request, CancellationToken cancellationToken) => Execute("result_analysis", request, false, cancellationToken);
    [HttpPost("get_assessment_longitudinal_analysis", Name = "get_assessment_longitudinal_analysis")]
    [ProducesResponseType(typeof(ToolResponseDto<AssessmentToolDataDto>), StatusCodes.Status200OK)]
    public Task<IActionResult> Multiple(AssessmentToolRequestDto request, CancellationToken cancellationToken) => Execute("longitudinal_analysis", request, false, cancellationToken);
    [HttpPost("generate_assessment_single_report", Name = "generate_assessment_single_report")]
    [ProducesResponseType(typeof(ToolResponseDto<AssessmentReportArtifactDto>), StatusCodes.Status200OK)]
    public Task<IActionResult> SingleReport(AssessmentToolRequestDto request, CancellationToken cancellationToken) => Execute("single_report", request, true, cancellationToken);
    [HttpPost("generate_assessment_longitudinal_report", Name = "generate_assessment_longitudinal_report")]
    [ProducesResponseType(typeof(ToolResponseDto<AssessmentReportArtifactDto>), StatusCodes.Status200OK)]
    public Task<IActionResult> MultipleReport(AssessmentToolRequestDto request, CancellationToken cancellationToken) => Execute("longitudinal_report", request, true, cancellationToken);

    private async Task<IActionResult> Execute(string operation, AssessmentToolRequestDto request, bool report, CancellationToken cancellationToken)
    {
        var tool = (report ? "generate_assessment_" : "get_assessment_") + operation;
        var id = HttpContext.TraceIdentifier;
        object Failure(string code, string message) => ToolResponseDto<object>.Create(tool, id, ToolStatuses.Unavailable, null,
            new[] { code }, new ToolMetaDto(), patientMessage: message, contractVersion: AssessmentToolOptions.ContractVersion);
        var trusted = _resolver.Resolve(HttpContext, request.SystemContext);
        if (!trusted.IsSuccess) return StatusCode(trusted.HttpStatusCode, Failure(trusted.ReasonCode!, "无法确认工具调用身份。"));
        try
        {
            return Ok(report
                ? (object)await _service.ReportAsync(operation == "longitudinal_report", trusted.Context!, request, $"{Request.Scheme}://{Request.Host}", id, cancellationToken)
                : await _service.ReadAsync(operation, trusted.Context!, request, id, cancellationToken));
        }
        catch (AssessmentReadLimitException) { return Ok(Failure("ASSESSMENT_READ_LIMIT_EXCEEDED", "记录超出当前查询上限。")); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { return StatusCode(503, Failure("ASSESSMENT_DEPENDENCY_UNAVAILABLE", "量表数据源暂不可用。")); }
    }
}
