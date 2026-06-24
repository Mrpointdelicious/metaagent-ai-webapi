/// 魏道好 2026/6/15
/// 内部调用接口
/// 仅作内部接口CURL联调使用

using AI_WebApi.IServices.AITools;
using AI_WebApi.Model;
using AI_WebApi.Model.DTOs.AITools;
using Microsoft.AspNetCore.Mvc;

namespace AI_WebApi.Controllers;

[ApiController]
[Route("api/ai/patients")]
public class AiPatientsController : ControllerBase
{
    private readonly IPatientRehabOverviewService _service;
    private readonly ILogger<AiPatientsController> _logger;

    public AiPatientsController(
        IPatientRehabOverviewService service,
        ILogger<AiPatientsController> logger)
    {
        _service = service;
        _logger = logger;
    }

    [HttpGet("{patientId:int}/rehab-overview")]
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
            [FromRoute] int patientId,
            [FromQuery] PatientRehabOverviewQueryDto query,
            CancellationToken cancellationToken)
    {
        if (patientId <= 0)
        {
            return BadRequest(
                ApiResponse<PatientRehabOverviewDto>.Error(
                    "patientId必须大于0",
                    StatusCodes.Status400BadRequest));
        }

        if (query.PastDays is < 1 or > 90)
        {
            return BadRequest(
                ApiResponse<PatientRehabOverviewDto>.Error(
                    "pastDays必须在1到90之间",
                    StatusCodes.Status400BadRequest));
        }

        if (query.FutureDays is < 1 or > 90)
        {
            return BadRequest(
                ApiResponse<PatientRehabOverviewDto>.Error(
                    "futureDays必须在1到90之间",
                    StatusCodes.Status400BadRequest));
        }

        try
        {
            // TODO: 接入登录态或Token后，在这里校验当前调用方
            // 是否有权访问该患者。无权访问时返回HTTP 403。

            var result = await _service.GetAsync(
                patientId,
                query.PastDays,
                query.FutureDays,
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
        catch (ArgumentOutOfRangeException exception)
        {
            _logger.LogWarning(
                exception,
                "患者康复概览参数错误，PatientId={PatientId}",
                patientId);

            return BadRequest(
                ApiResponse<PatientRehabOverviewDto>.Error(
                    "查询参数不正确",
                    StatusCodes.Status400BadRequest));
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            // 客户端已断开或主动取消，不转换成500。
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "患者康复概览查询失败，PatientId={PatientId}",
                patientId);

            return StatusCode(
                StatusCodes.Status500InternalServerError,
                ApiResponse<PatientRehabOverviewDto>.Error(
                    "服务器内部异常，无法完成查询",
                    StatusCodes.Status500InternalServerError));
        }
    }
}