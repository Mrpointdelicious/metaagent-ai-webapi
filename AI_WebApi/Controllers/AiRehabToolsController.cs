using AI_WebApi.IServices.AITools;
using AI_WebApi.Model;
using AI_WebApi.Model.DTOs.AITools;
using AI_WebApi.Security;
using Microsoft.AspNetCore.Mvc;

namespace AI_WebApi.Controllers;

[ApiController]
[Route("api/ai/rehab/tools")]
[ApiExplorerSettings(GroupName = "ai-rehab-tools-v1")]
[Produces("application/json")]
public sealed class AiRehabToolsController : ControllerBase
{
    private readonly IDoctorToolsService _doctorService;
    private readonly ISceneNavigationService _sceneService;
    private readonly IDifyServiceRequestAuthenticator _authenticator;
    private readonly ILogger<AiRehabToolsController> _logger;

    public AiRehabToolsController(
        IDoctorToolsService doctorService,
        ISceneNavigationService sceneService,
        IDifyServiceRequestAuthenticator authenticator,
        ILogger<AiRehabToolsController> logger)
    {
        _doctorService = doctorService;
        _sceneService = sceneService;
        _authenticator = authenticator;
        _logger = logger;
    }

    [HttpPost("search_doctors", Name = "search_doctors")]
    [ProducesResponseType(typeof(ApiResponse<SearchDoctorsToolResponseDto>), 200)]
    [ProducesResponseType(typeof(ApiResponse<SearchDoctorsToolResponseDto>), 401)]
    [ProducesResponseType(typeof(ApiResponse<SearchDoctorsToolResponseDto>), 502)]
    public async Task<ActionResult<ApiResponse<SearchDoctorsToolResponseDto>>>
        SearchDoctors(
            [FromBody] SearchDoctorsToolRequestDto? request,
            CancellationToken cancellationToken)
    {
        var authentication = _authenticator.Authenticate(HttpContext);
        if (!authentication.IsSuccess)
        {
            return StatusCode(
                authentication.HttpStatusCode,
                ApiResponse<SearchDoctorsToolResponseDto>.Error(
                    "工具服务认证失败",
                    authentication.HttpStatusCode));
        }
        try
        {
            var result = await _doctorService.SearchDoctorsAsync(
                request?.SpaceId,
                cancellationToken);
            return Ok(ApiResponse<SearchDoctorsToolResponseDto>.Success(
                result,
                "查询成功"));
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "search_doctors failed");
            return StatusCode(
                StatusCodes.Status502BadGateway,
                ApiResponse<SearchDoctorsToolResponseDto>.Error(
                    "在线医生服务暂时不可用",
                    StatusCodes.Status502BadGateway));
        }
    }

    [HttpPost("navigate_scene", Name = "navigate_scene")]
    [ProducesResponseType(typeof(ApiResponse<SceneOperationToolResponseDto>), 200)]
    [ProducesResponseType(typeof(ApiResponse<SceneOperationToolResponseDto>), 400)]
    [ProducesResponseType(typeof(ApiResponse<SceneOperationToolResponseDto>), 401)]
    [ProducesResponseType(typeof(ApiResponse<SceneOperationToolResponseDto>), 502)]
    public async Task<ActionResult<ApiResponse<SceneOperationToolResponseDto>>>
        NavigateScene(
            [FromBody] SceneOperationToolRequestDto? request,
            CancellationToken cancellationToken)
    {
        var authentication = _authenticator.Authenticate(HttpContext);
        if (!authentication.IsSuccess)
        {
            return StatusCode(
                authentication.HttpStatusCode,
                ApiResponse<SceneOperationToolResponseDto>.Error(
                    "工具服务认证失败",
                    authentication.HttpStatusCode));
        }
        if (request == null || string.IsNullOrWhiteSpace(request.Target))
        {
            return BadRequest(ApiResponse<SceneOperationToolResponseDto>.Error(
                "target required",
                StatusCodes.Status400BadRequest));
        }
        try
        {
            var result = await _sceneService.NavigateAsync(
                request.Target,
                request.SpaceId,
                cancellationToken);
            return Ok(ApiResponse<SceneOperationToolResponseDto>.Success(
                result,
                "OK"));
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
                "navigate_scene failed, target={Target}",
                request.Target);
            return StatusCode(
                StatusCodes.Status502BadGateway,
                ApiResponse<SceneOperationToolResponseDto>.Error(
                    "场景操作服务暂时不可用",
                    StatusCodes.Status502BadGateway));
        }
    }
}
