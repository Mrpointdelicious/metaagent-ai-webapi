using AI_WebApi.IServices.AITools;
using AI_WebApi.Model.Domain.IReGo;
using AI_WebApi.Model.DTOs.AITools;
using AI_WebApi.Services.AITools.PatientContext;
using AI_WebApi.Model.AITools;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Newtonsoft.Json.Linq;

namespace AI_WebApi.Security;

public sealed class PatientV2ScopeFilter : IAsyncActionFilter
{
    public static readonly object ContextItemKey = new();
    private readonly IDifyServiceRequestAuthenticator _authenticator;
    private readonly IPatientAccessContextResolver _resolver;
    private readonly IPatientAccessContextAccessor _accessor;
    private readonly ILogger<PatientV2ScopeFilter> _logger;
    private readonly PatientAccessOptions _options;
    public PatientV2ScopeFilter(IDifyServiceRequestAuthenticator authenticator, IPatientAccessContextResolver resolver,
        IPatientAccessContextAccessor accessor, ILogger<PatientV2ScopeFilter> logger, IOptions<PatientAccessOptions> options)
    { _authenticator = authenticator; _resolver = resolver; _accessor = accessor; _logger = logger; _options = options.Value; }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var auth = _authenticator.Authenticate(context.HttpContext);
        if (!auth.IsSuccess) { Fail(context, auth.HttpStatusCode, auth.ReasonCode!); return; }
        if (HttpMethods.IsGet(context.HttpContext.Request.Method))
        {
            var catalog = await next();
            if (catalog.Result is ObjectResult { Value: not null } result)
            {
                var json = JObject.FromObject(result.Value);
                json["contract_version"] = "2.0.0";
                json["tool_name"] = "get_patient_tool_catalog";
                result.Value = json; result.DeclaredType = null;
            }
            return;
        }
        var request = context.ActionArguments.Values.FirstOrDefault(v => v?.GetType().GetProperty("SystemContext") != null);
        var system = request?.GetType().GetProperty("SystemContext")?.GetValue(request) as DifySystemContextDto;
        var scope = request?.GetType().GetProperty("DataScope")?.GetValue(request) as string;
        if (context.ActionDescriptor.AttributeRouteInfo?.Name?.StartsWith("generate_", StringComparison.Ordinal) == true &&
            string.IsNullOrWhiteSpace(request?.GetType().GetProperty("ExpectedSourceSnapshot")?.GetValue(request) as string))
        { Fail(context, 400, "EXPECTED_SOURCE_SNAPSHOT_REQUIRED"); return; }
        PatientAccessResolution resolved;
        try { resolved = await _resolver.ResolveAsync(system, scope, context.HttpContext.TraceIdentifier, context.HttpContext.RequestAborted); }
        catch (OperationCanceledException) when (context.HttpContext.RequestAborted.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Patient v2 identity resolution failed. RequestId={RequestId}", context.HttpContext.TraceIdentifier);
            Fail(context, 503, "PATIENT_IDENTITY_DEPENDENCY_FAILURE"); return;
        }
        if (!resolved.IsSuccess) { Fail(context, resolved.HttpStatusCode, resolved.ReasonCode!); return; }
        context.HttpContext.Items[ContextItemKey] = resolved.Context;
        _accessor.Current = resolved.Context!.Access;
        try
        {
            var executed = await next();
            if (executed.Result is not ObjectResult { Value: not null } response) return;
            var value = response.Value;
            var data = value.GetType().GetProperty("Data")?.GetValue(value);
            RecordTenantPresentation.Annotate(data, _accessor.Current);
            var json = JObject.FromObject(value);
            var access = resolved.Context.Access!;
            var description = access.Describe();
            json["query_scope"] = JObject.FromObject(description);
            json["contract_version"] = "2.0.0";
            var segments = RecordTenantPresentation.Segments(data, access);
            if (json["data"] is JObject artifactData && artifactData["tenant_segments"] is JArray artifactSegments)
                segments = artifactSegments.ToObject<PatientTenantSegmentDto[]>() ?? Array.Empty<PatientTenantSegmentDto>();
            if (segments.Count > 0)
            {
                json["tenant_segments"] = JArray.FromObject(segments);
                json["query_scope"]!["included_tenant_count"] = segments.Where(s => s.RecordTenant.TenantRef != null).Select(s => s.RecordTenant.TenantRef).Distinct().Count();
                json["query_scope"]!["unattributed_tenant_record_count"] = segments.Where(s => s.RecordTenant.AttributionStatus != "verified").Sum(s => s.RecordCount);
            }
            json["tool_name"] = (json.Value<string>("tool_name") ?? "patient_tool");
            var reasons = (json["reason_codes"]?.Values<string>() ?? Enumerable.Empty<string>()).Concat(description.Issues).Distinct().ToArray();
            json["reason_codes"] = JArray.FromObject(reasons);
            if (description.Coverage == "partial" && json.Value<string>("status") == ToolStatuses.Success) json["status"] = ToolStatuses.Partial;
            if (json["data"] is JObject artifact && artifact.Value<string>("artifact_ref") is { } reference && reference.StartsWith("irego_scope_v2_", StringComparison.Ordinal))
            {
                var nowUtc = DateTimeOffset.UtcNow;
                var proof = PatientIdentityProofCodec.VerifyForAccess(system?.IdentityContextToken, _options, nowUtc);
                if (proof == null && !_options.RequireIdentityProof)
                {
                    var nowSeconds = nowUtc.ToUnixTimeSeconds();
                    var lifetime = Math.Clamp(_options.MaximumProofLifetimeSeconds, 30, 900);
                    proof = new PatientIdentityProof
                    {
                        Purpose = "patient_rehab_v2",
                        Subject = access.Subject,
                        AnchorUserId = access.AnchorUserId,
                        ActorRole = access.ActorRole,
                        CurrentBusinessId = access.CurrentBusinessId,
                        AllowAllTenants = access.DataScope == "all_tenants",
                        IssuedAt = nowSeconds,
                        ExpiresAt = nowSeconds + lifetime
                    };
                }
                if (proof == null) { Fail(context, 401, "PATIENT_IDENTITY_PROOF_INVALID"); return; }
                var expiry = Math.Min(nowUtc.AddMinutes(5).ToUnixTimeSeconds(), proof.ExpiresAt);
                var grant = PatientArtifactGrantCodec.Issue(new(reference, access.ScopeFingerprint, access.DataScope, proof, expiry), _options.IdentitySigningKey);
                var oldUrl = artifact.Value<string>("image_url");
                var origin = Uri.TryCreate(oldUrl, UriKind.Absolute, out var original) ? original.GetLeftPart(UriPartial.Authority) : $"{context.HttpContext.Request.Scheme}://{context.HttpContext.Request.Host}";
                var url = origin + "/api/ai/patients/report-artifacts/" + Uri.EscapeDataString(reference) + "/content?access_grant=" + Uri.EscapeDataString(grant);
                artifact["image_url"] = url;
                artifact["display_markdown"] = "![康复记录报告](" + url + ")";
                artifact["expires_at"] = DateTimeOffset.FromUnixTimeSeconds(expiry);
            }
            response.Value = json; response.DeclaredType = null;
        }
        finally { _accessor.Current = null; context.HttpContext.Items.Remove(ContextItemKey); }
    }

    private static void Fail(ActionExecutingContext context, int status, string code) => context.Result = new ObjectResult(
        ToolResponseDto<object>.Create("patient_tool_v2", context.HttpContext.TraceIdentifier, ToolStatuses.Unavailable, null,
            new[] { code }, new ToolMetaDto(), "无法确认患者身份或查询范围。", "2.0.0")) { StatusCode = status };
}
