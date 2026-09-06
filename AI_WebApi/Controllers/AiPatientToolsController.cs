/// 魏道好 2026/6/15
/// Dify使用接口

using AI_WebApi.IServices.AITools;
using AI_WebApi.Model;
using AI_WebApi.Model.DTOs.AITools;
using Microsoft.AspNetCore.Mvc;

namespace AI_WebApi.Controllers;

[ApiController]
[Route("api/ai/patients/tools")]
[ApiExplorerSettings(IgnoreApi = true)]
[Produces("application/json")]
public class AiPatientToolsController : ControllerBase
{
    private readonly IPatientRehabOverviewService _service;
    private readonly ILogger<AiPatientToolsController> _logger;

    public AiPatientToolsController(
        IPatientRehabOverviewService service,
        ILogger<AiPatientToolsController> logger)
    {
        _service = service;
        _logger = logger;
    }

    [HttpPost(
        "get_patient_rehab_overview",
        Name = "get_patient_rehab_overview")]
    [ProducesResponseType(
        typeof(ApiResponse<PatientRehabOverviewDto>),
        StatusCodes.Status200OK)]
    [ProducesResponseType(
        typeof(ApiResponse<PatientRehabOverviewDto>),
        StatusCodes.Status400BadRequest)]
    [ProducesResponseType(
        typeof(ApiResponse<PatientRehabOverviewDto>),
        StatusCodes.Status404NotFound)]
    [ProducesResponseType(
        typeof(ApiResponse<PatientRehabOverviewDto>),
        StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ApiResponse<PatientRehabOverviewDto>>>
        GetPatientRehabOverview(
            [FromBody] GetPatientRehabOverviewToolRequestDto request,
            CancellationToken cancellationToken)
    {
        if (request == null)
        {
            return BadRequest(
                ApiResponse<PatientRehabOverviewDto>.Error(
                    "请求体不能为空",
                    StatusCodes.Status400BadRequest));
        }

        if (request.PatientId <= 0)
        {
            return BadRequest(
                ApiResponse<PatientRehabOverviewDto>.Error(
                    "patientId必须大于0",
                    StatusCodes.Status400BadRequest));
        }

        const int pastDays = 30;
        const int futureDays = 30;

        try
        {
            // TODO: 后续接入登录态或 Token 后，在这里校验当前调用方
            // 是否有权访问 request.PatientId。无权访问时返回 HTTP 403。

            var result = await _service.GetAsync(
            request.PatientId,
            pastDays,
            futureDays,
            cancellationToken);
            if (result == null)
            {
                return NotFound(
                    ApiResponse<PatientRehabOverviewDto>.Error(
                        "患者不存在",
                        StatusCodes.Status404NotFound));
            }

            return Ok(
                ApiResponse<PatientRehabOverviewDto>.Success(
                    result,
                    "患者康复概览查询成功"));
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
                "Dify患者康复概览工具调用失败，PatientId={PatientId}",
                request.PatientId);

            return StatusCode(
                StatusCodes.Status500InternalServerError,
                ApiResponse<PatientRehabOverviewDto>.Error(
                    "服务器内部异常，无法完成查询",
                    StatusCodes.Status500InternalServerError));
        }
    }
}
