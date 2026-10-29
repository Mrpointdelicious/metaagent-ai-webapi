using AI_WebApi.Model.Domain.IReGo;
using AI_WebApi.Model.DTOs.AITools;
using AI_WebApi.Security;
using Microsoft.AspNetCore.Mvc;

namespace AI_WebApi.Controllers;

[ApiController]
[Route("api/ai/patients/tools")]
[ApiExplorerSettings(GroupName = PatientV2RoutingConvention.GroupName)]
[ServiceFilter(typeof(PatientV2ScopeFilter))]
public sealed class PatientAccessV2Controller : ControllerBase
{
    [HttpPost("resolve_patient_identity", Name = "resolve_patient_identity")]
    public ActionResult<ToolResponseDto<PatientAccessResolutionDto>> Resolve(ResolvePatientAccessRequestDto request)
    {
        var context = (TrustedPatientContext)HttpContext.Items[PatientV2ScopeFilter.ContextItemKey]!;
        return Ok(ToolResponseDto<PatientAccessResolutionDto>.Create("resolve_patient_identity", HttpContext.TraceIdentifier,
            ToolStatuses.Success, new PatientAccessResolutionDto { PatientRef = context.PatientRef, IncludedAccountCount = context.Access!.UserIds.Length },
            null, new ToolMetaDto(), contractVersion: "2.0.0"));
    }
}
