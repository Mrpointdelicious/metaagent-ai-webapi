using AI_WebApi.IServices.AITools;
using AI_WebApi.Model.AITools;
using AI_WebApi.Model.DTOs.AITools;
using AI_WebApi.Services.AITools.PatientContext;
using AI_WebApi.Security;
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

    [HttpPost("/api/ai/patients/report-artifacts/{artifactRef}/content")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> ReadForChat(string artifactRef, [FromBody] ReadPatientReportRequest request,
        [FromServices] IDifyServiceRequestAuthenticator authenticator, CancellationToken ct)
    {
        var service = authenticator.Authenticate(HttpContext);
        if (!service.IsSuccess) return StatusCode(service.HttpStatusCode);

        var previous = PatientArtifactGrantCodec.ReadSignedBinding(request.OriginalGrant, _options.IdentitySigningKey);
        var current = request.SystemContext;
        if (previous == null || current == null || previous.ArtifactRef != artifactRef ||
            previous.Identity.Subject != current.User || previous.DataScope != request.DataScope)
            return StatusCode(403);

        // In the existing owner-only chat history contract the authenticated gateway
        // supplies the viewer. Use the signed original account as a lookup selector
        // for self reports only. A doctor's delegation always needs a current proof.
        if (current.PatientIdentity == null && !_options.RequireIdentityProof &&
            string.IsNullOrEmpty(current.IdentityContextToken) && request.DataScope == "all_tenants" &&
            previous.Identity.ActorRole is ("patient" or "student"))
        {
            current = new DifySystemContextDto
            {
                User = current.User, ConversationId = current.ConversationId, CurrentBusinessId = current.CurrentBusinessId,
                PatientIdentity = new PatientIdentityInputDto { Type = "project_user_id",
                    Value = previous.Identity.AnchorUserId.ToString(System.Globalization.CultureInfo.InvariantCulture) }
            };
        }
        var resolved = await _resolver.ResolveAsync(current, request.DataScope, HttpContext.TraceIdentifier, ct);
        if (!resolved.IsSuccess) return StatusCode(resolved.HttpStatusCode);
        var access = resolved.Context!.Access!;
        if (access.AnchorUserId != previous.Identity.AnchorUserId || access.ScopeFingerprint != previous.ScopeFingerprint)
            return StatusCode(403);
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return _store.TryGet(artifactRef, out var bytes) ? File(bytes, "image/png") : NotFound();
    }

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
