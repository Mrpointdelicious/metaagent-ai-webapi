using AI_WebApi.IServices.AITools;
using AI_WebApi.Model.AITools;
using AI_WebApi.Model.DTOs.AITools;
using AI_WebApi.Services.AITools.PatientContext;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace AI_WebApi.Controllers;

[ApiController]
[ApiExplorerSettings(IgnoreApi = true)]
public sealed class PatientReportArtifactsV2Controller : ControllerBase
{
    private readonly IPatientAccessContextResolver _resolver;
    private readonly IIReGoReportArtifactStore _store;
    private readonly PatientAccessOptions _options;
    public PatientReportArtifactsV2Controller(IPatientAccessContextResolver resolver, IIReGoReportArtifactStore store, IOptions<PatientAccessOptions> options)
    { _resolver = resolver; _store = store; _options = options.Value; }

    [HttpGet("/api/ai/patients/report-artifacts/{artifactRef}/content")]
    public async Task<IActionResult> Content(string artifactRef, [FromQuery(Name = "access_grant")] string? token, CancellationToken ct)
    {
        Response.Headers["Cache-Control"] = "private, no-store, max-age=0";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        var grant = PatientArtifactGrantCodec.Verify(token, _options.IdentitySigningKey, DateTimeOffset.UtcNow);
        if (grant == null || grant.ArtifactRef != artifactRef) return Unauthorized();
        var identity = grant.Identity;
        var resolved = await _resolver.ResolveAsync(new DifySystemContextDto
        {
            User = identity.Subject,
            PatientIdentity = new() { Type = "project_user_id", Value = identity.AnchorUserId.ToString(System.Globalization.CultureInfo.InvariantCulture) },
            CurrentBusinessId = identity.CurrentBusinessId,
            IdentityContextToken = PatientIdentityProofCodec.IssueForAccess(identity, _options)
        }, grant.DataScope, HttpContext.TraceIdentifier, ct);
        if (!resolved.IsSuccess || resolved.Context!.Access!.ScopeFingerprint != grant.ScopeFingerprint) return StatusCode(403);
        return _store.TryGet(artifactRef, out var bytes) ? File(bytes, "image/png") : NotFound();
    }
}
