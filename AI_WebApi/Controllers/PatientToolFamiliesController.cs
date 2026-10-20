using AI_WebApi.IServices.AITools;
using AI_WebApi.Model.AITools;
using AI_WebApi.Model.DTOs.AITools;
using AI_WebApi.Security;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace AI_WebApi.Controllers;

[ApiController]
[Route("api/ai/patients/tools")]
[ApiExplorerSettings(GroupName = "ai-patient-tools-v1")]
public sealed class PatientToolFamiliesController : ControllerBase
{
    private readonly IDifyServiceRequestAuthenticator _authenticator;
    private readonly IDifySystemContextResolver _resolver;
    private readonly IIReGoPatientEvaluationService _go;
    public PatientToolFamiliesController(IDifyServiceRequestAuthenticator authenticator, IDifySystemContextResolver resolver, IIReGoPatientEvaluationService go)
    { _authenticator = authenticator; _resolver = resolver; _go = go; }

    [HttpGet("catalog", Name = "get_patient_tool_catalog")]
    [ProducesResponseType(typeof(ToolResponseDto<IReadOnlyList<PatientToolFamily>>), StatusCodes.Status200OK)]
    public IActionResult Catalog()
    {
        var auth = _authenticator.Authenticate(HttpContext);
        var body = ToolResponseDto<IReadOnlyList<PatientToolFamily>>.Create("get_patient_tool_catalog", HttpContext.TraceIdentifier,
            auth.IsSuccess ? ToolStatuses.Success : ToolStatuses.Unavailable, auth.IsSuccess ? PatientToolFamilies.All : null,
            auth.IsSuccess ? null : new[] { auth.ReasonCode! }, new ToolMetaDto(), contractVersion: PatientToolFamilies.Version);
        return StatusCode(auth.HttpStatusCode, body);
    }

    [HttpPost("get_irego_patient_context", Name = "get_irego_patient_context")]
    [ProducesResponseType(typeof(ToolResponseDto<TrainingOverviewDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GoOverview(TrainingOverviewRequestDto request, CancellationToken cancellationToken)
    {
        ToolResponseDto<TrainingOverviewDto> Response(string status, TrainingOverviewDto? data, string? code = null) =>
            ToolResponseDto<TrainingOverviewDto>.Create("get_irego_patient_context", HttpContext.TraceIdentifier, status, data,
                code == null ? null : new[] { code }, new ToolMetaDto(), contractVersion: IReGoContract.Version);
        var trusted = _resolver.Resolve(HttpContext, request.SystemContext);
        if (!trusted.IsSuccess) return StatusCode(trusted.HttpStatusCode, Response(ToolStatuses.Unavailable, null, trusted.ReasonCode));
        try
        {
            var evaluation = await _go.EvaluateAsync(trusted.Context!, cancellationToken);
            if (evaluation.Raw.Patient == null || evaluation.Raw.Patient.Id != trusted.Context!.PatientId || evaluation.Raw.Patient.IsDeleted == true)
                return Ok(Response(ToolStatuses.Unavailable, null, "PATIENT_NOT_FOUND"));
            return Ok(Response(ToolStatuses.Success, new TrainingOverviewDto
            {
                ObjectType = "irego", DataState = evaluation.DataState,
                Counts = new Dictionary<string, int>
                {
                    ["plan_count"] = evaluation.StrictPlanCount, ["report_count"] = evaluation.StrictReportCount,
                    ["usable_session_count"] = evaluation.Sessions.Count(s => s.Usable && s.EligibleForLatest),
                    ["voided_plan_count"] = evaluation.VoidedPlanCount, ["reference_anomaly_count"] = evaluation.ReferenceAnomalyCount
                }
            }));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { return StatusCode(503, Response(ToolStatuses.Unavailable, null, "IREGO_DEPENDENCY_UNAVAILABLE")); }
    }
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class TrainingOverviewRequestDto { public DifySystemContextDto? SystemContext { get; init; } }
[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class TrainingOverviewDto
{
    public string ObjectType { get; init; } = string.Empty;
    public string DataState { get; init; } = "unknown";
    public IReadOnlyDictionary<string, int> Counts { get; init; } = new Dictionary<string, int>();
}
