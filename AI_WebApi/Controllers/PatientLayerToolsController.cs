using AI_WebApi.IServices.AITools;
using AI_WebApi.Model.Domain.IReGo;
using AI_WebApi.Model.DTOs.AITools;
using AI_WebApi.Security;
using Microsoft.AspNetCore.Mvc;

namespace AI_WebApi.Controllers;

[ApiController]
[Route("api/ai/patients/tools")]
[ApiExplorerSettings(GroupName = "ai-patient-tools-v2")]
[Produces("application/json")]
public sealed class PatientLayerToolsController : ControllerBase
{
    private readonly IPatientLayerService _service;
    private readonly IDifySystemContextResolver _resolver;
    private readonly ILogger<PatientLayerToolsController> _logger;

    public PatientLayerToolsController(IPatientLayerService service, IDifySystemContextResolver resolver,
        ILogger<PatientLayerToolsController> logger)
    { _service = service; _resolver = resolver; _logger = logger; }

    [HttpPost("get_patient_profile", Name = "get_patient_profile")]
    public Task<ActionResult<ToolResponseDto<PatientProfileDto>>> Profile(
        [FromBody] PatientLayerRequestDto? request, CancellationToken ct) => Execute(
            "get_patient_profile", request, ct, (context, rid) => _service.ProfileAsync(context, rid, ct));

    [HttpPost("get_patient_consultation", Name = "get_patient_consultation")]
    public Task<ActionResult<ToolResponseDto<PatientConsultationDto>>> Consultation(
        [FromBody] PatientLayerRequestDto? request, CancellationToken ct) => Execute(
            "get_patient_consultation", request, ct, (context, rid) => _service.ConsultationAsync(context, request!, rid, ct));

    [HttpPost("get_patient_rehab", Name = "get_patient_rehab")]
    public Task<ActionResult<ToolResponseDto<PatientRehabDto>>> Rehab(
        [FromBody] PatientLayerRequestDto? request, CancellationToken ct) => Execute(
            "get_patient_rehab", request, ct, (context, rid) => _service.RehabAsync(context, request!, rid, ct));

    private async Task<ActionResult<ToolResponseDto<T>>> Execute<T>(string tool,
        PatientLayerRequestDto? request, CancellationToken ct,
        Func<TrustedPatientContext, string, Task<ToolResponseDto<T>>> action)
    {
        var rid = HttpContext.TraceIdentifier;
        if (request == null || request.PageNumber is < 1 or > 100000 || request.PageSize is < 1 or > 20)
            return BadRequest(Failure<T>(tool, rid, "INVALID_REQUEST"));
        var resolved = _resolver.Resolve(HttpContext, request.SystemContext);
        if (!resolved.IsSuccess)
            return StatusCode(resolved.HttpStatusCode, Failure<T>(tool, rid, resolved.ReasonCode!));
        try { return Ok(await action(resolved.Context!, rid)); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Patient layer query failed. Tool={Tool}, RequestId={RequestId}", tool, rid);
            return StatusCode(503, Failure<T>(tool, rid, "DEPENDENCY_FAILURE"));
        }
    }

    private static ToolResponseDto<T> Failure<T>(string tool, string rid, string reason) =>
        ToolResponseDto<T>.Create(tool, rid, ToolStatuses.Failed, default, new[] { reason },
            new ToolMetaDto(), "患者信息查询暂未完成。", PatientLayerContract.Version);
}
