using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using AI_WebApi.Model.Entities;
using AI_WebApi.Repository.AITools;
using AI_WebApi.Security;
using Microsoft.AspNetCore.Mvc;

namespace AI_WebApi.Controllers;

[ApiController]
[Route("api/ai/patients/me")]
[ApiExplorerSettings(GroupName = PatientV2RoutingConvention.GroupName)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class PatientSelfController : ControllerBase
{
    private readonly IConfiguration _config;
    private readonly IHttpClientFactory _clients;
    private readonly Lazy<IPatientAccessRepository> _accounts;
    private readonly Lazy<IPatientSelfReportCount> _reports;

    public PatientSelfController(IConfiguration config, IHttpClientFactory clients,
        Lazy<IPatientAccessRepository> accounts, Lazy<IPatientSelfReportCount> reports) =>
        (_config, _clients, _accounts, _reports) = (config, clients, accounts, reports);

    [HttpGet("rehab-summary")]
    public async Task<IActionResult> RehabSummary(CancellationToken ct)
    {
        if (Request.Query.Count != 0) return BadRequest();
        var header = Request.Headers["Authorization"];
        if (header.Count != 1 || !AuthenticationHeaderValue.TryParse(header[0], out var auth) ||
            !string.Equals(auth.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrEmpty(auth.Parameter) || auth.Parameter.Length > 8192 ||
            auth.Parameter.Any(ch => ch <= ' ' || ch > '~' || ch == ',')) return Unauthorized();
        if (!Uri.TryCreate(_config["PatientSelf:SessionUrl"], UriKind.Absolute, out var url) ||
            url.Scheme != Uri.UriSchemeHttps || url.UserInfo.Length + url.Query.Length + url.Fragment.Length != 0)
            return StatusCode(503);
        try
        {
            using var client = _clients.CreateClient("patient-self-session");
            using var message = new HttpRequestMessage(HttpMethod.Get, url);
            message.Headers.Authorization = auth;
            using var result = await client.SendAsync(message, ct);
            if (result.StatusCode == HttpStatusCode.Unauthorized) return Unauthorized();
            if (result.StatusCode == HttpStatusCode.Forbidden) return StatusCode(403);
            if (result.StatusCode != HttpStatusCode.OK) return StatusCode(503);
            using var json = JsonDocument.Parse(await result.Content.ReadAsStringAsync(ct));
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("active", out var active) ||
                active.ValueKind != JsonValueKind.True || !root.TryGetProperty("login_name", out var login) ||
                login.ValueKind != JsonValueKind.String || !root.TryGetProperty("current_business_id", out var tenant) ||
                tenant.ValueKind != JsonValueKind.Number || !tenant.TryGetInt32(out var businessId) || businessId <= 0)
                return StatusCode(403);
            var name = login.GetString();
            if (string.IsNullOrWhiteSpace(name) || name.Length > 64 || name != name.Trim()) return StatusCode(403);
            var accounts = _accounts.Value;
            var matches = await accounts.FindAnchorAsync("login_name", name, ct);
            if (matches.Count != 1 || matches[0] is not { Id: > 0, BusinessId: 0, Status: 1, IsDeleted: false })
                return StatusCode(403);
            var id = matches[0].Id;
            var access = await accounts.LoadAccessAsync(id, ct);
            if (!access.Memberships.Any(m => m.UserId == id && m.BusinessId == businessId && m.Enable == 0) ||
                !access.Roles.Any(r => r.UserId == id && r.BusinessId == businessId && r.Enable == 0 &&
                    r.RoleId is DbRoleUserEntity.PatientRoleId or DbRoleUserEntity.StudentRoleId)) return StatusCode(403);
            return Ok(new { record_count = await _reports.Value.CountAsync(id, businessId, ct) });
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception) { return StatusCode(503); }
    }
}
