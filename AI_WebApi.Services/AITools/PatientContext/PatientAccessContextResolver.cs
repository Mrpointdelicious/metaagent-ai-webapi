using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using AI_WebApi.IServices.AITools;
using AI_WebApi.Model.AITools;
using AI_WebApi.Model.Domain.IReGo;
using AI_WebApi.Model.DTOs.AITools;
using AI_WebApi.Model.Entities;
using AI_WebApi.Repository.AITools;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AI_WebApi.Services.AITools.PatientContext;

public sealed class PatientAccessContextAccessor : IPatientAccessContextAccessor
{
    public PatientAccessContext? Current { get; set; }
}

public sealed class PatientAccessContextResolver : IPatientAccessContextResolver
{
    private readonly IPatientAccessRepository _repository;
    private readonly PatientAccessOptions _options;
    public PatientAccessContextResolver(IPatientAccessRepository repository, IOptions<PatientAccessOptions> options)
    { _repository = repository; _options = options.Value; }

    public async Task<PatientAccessResolution> ResolveAsync(DifySystemContextDto? input, string? dataScope, string requestId, CancellationToken ct)
    {
        static PatientAccessResolution Deny(string reason, int status = 401) => new(null, reason, status);
        if (Encoding.UTF8.GetByteCount(_options.IdentitySigningKey) < 32) return Deny("PATIENT_IDENTITY_SIGNING_NOT_CONFIGURED", 503);
        var scope = dataScope ?? "all_tenants";
        if (scope is not ("all_tenants" or "current_tenant")) return Deny("INVALID_DATA_SCOPE", 400);
        var proof = PatientIdentityProofCodec.Verify(input?.IdentityContextToken, _options.IdentitySigningKey, DateTimeOffset.UtcNow, _options.MaximumProofLifetimeSeconds);
        if (proof == null || input?.User != proof.Subject) return Deny("PATIENT_IDENTITY_PROOF_INVALID");
        if (scope == "all_tenants" && !proof.AllowAllTenants) return Deny("PATIENT_SCOPE_FORBIDDEN", 403);
        if (input.CurrentBusinessId.HasValue && input.CurrentBusinessId != proof.CurrentBusinessId) return Deny("CURRENT_TENANT_MISMATCH", 403);
        if (input.ConversationId?.Length > 256) return Deny("INVALID_DIFY_SYSTEM_CONTEXT", 400);
        var identity = input.PatientIdentity;
        if (identity == null || identity.Type is not ("project_user_id" or "login_name") ||
            string.IsNullOrWhiteSpace(identity.Value) || identity.Value.Length > 64 || identity.Value != identity.Value.Trim())
            return Deny("INVALID_PATIENT_IDENTITY", 400);
        var anchors = await _repository.FindAnchorAsync(identity.Type, identity.Value, ct);
        if (anchors.Count != 1) return Deny(identity.Type == "login_name" && anchors.Count > 1 ? "LOGIN_NAME_NOT_UNIQUE" : "PATIENT_IDENTITY_NOT_FOUND", 404);
        var anchor = anchors[0];
        if (anchor.Id != proof.AnchorUserId) return Deny("PATIENT_IDENTITY_MISMATCH", 403);
        if (anchor.Status != 1 || anchor.IsDeleted == true) return Deny("PATIENT_ACCOUNT_UNAVAILABLE", 403);
        var explicitIds = new HashSet<int> { anchor.Id };
        for (var pass = 0; pass < _options.ConfirmedAccountGroups.Count; pass++)
            foreach (var group in _options.ConfirmedAccountGroups.Where(g => g.Any(explicitIds.Contains))) explicitIds.UnionWith(group.Where(id => id > 0));
        if (explicitIds.Count > 100) return Deny("PATIENT_IDENTITY_READ_LIMIT", 503);
        var source = await _repository.LoadRelatedAsync(anchor, explicitIds.ToArray(), ct);
        bool PatientRole(int id) => source.Roles.Any(r => r.UserId == id && r.RoleId == 0 && r.Enable == 0);
        if (!PatientRole(anchor.Id)) return Deny("PATIENT_SELF_ACCESS_REQUIRED", 403);
        if (scope == "current_tenant" && (proof.CurrentBusinessId is not > 0 ||
            !source.Memberships.Any(t => t.UserId == anchor.Id && t.BusinessId == proof.CurrentBusinessId && t.Enable == 0) ||
            !source.Roles.Any(r => r.UserId == anchor.Id && r.BusinessId == proof.CurrentBusinessId && r.RoleId == 0 && r.Enable == 0)))
            return Deny("CURRENT_TENANT_ACCESS_DENIED", 403);

        var ids = new HashSet<int> { anchor.Id };
        var issues = new HashSet<string>();
        var phoneProof = !string.IsNullOrWhiteSpace(anchor.Phone) && !string.IsNullOrEmpty(proof.PhoneBindingHash) &&
            CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(proof.PhoneBindingHash),
                Encoding.UTF8.GetBytes(PatientIdentityProofCodec.PhoneBindingHash(anchor.Phone, _options.IdentitySigningKey)));
        foreach (var candidate in source.Accounts.Where(u => u.Id != anchor.Id))
        {
            if (explicitIds.Contains(candidate.Id)) { ids.Add(candidate.Id); continue; }
            if (!phoneProof) { issues.Add("PHONE_ASSOCIATION_UNVERIFIED"); continue; }
            if (candidate.Status != 1 || candidate.IsDeleted == true || !PatientRole(candidate.Id))
            { issues.Add("HISTORICAL_ACCOUNT_BINDING_UNVERIFIED"); continue; }
            if (candidate.Phone?.Trim() != anchor.Phone?.Trim() || !Compatible(anchor, candidate))
            { issues.Add("IDENTITY_LINK_AMBIGUOUS"); continue; }
            ids.Add(candidate.Id);
        }
        var userIds = ids.OrderBy(id => id).ToArray();
        var walks = _options.ConfirmedWalkRecords.Where(w => ids.Contains(w.ProjectUserId) && w.WalkId > 0)
            .GroupBy(w => w.WalkId).Where(g => g.Select(w => (w.ProjectUserId, w.BusinessId)).Distinct().Count() == 1)
            .ToDictionary(g => g.Key, g => g.First());
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new
        {
            version = "patient-access-v2", proof.Subject, anchor.Id, scope,
            tenant = scope == "current_tenant" ? proof.CurrentBusinessId : null,
            owners = userIds, phone_binding = PatientIdentityProofCodec.PhoneBindingHash(anchor.Phone, _options.IdentitySigningKey),
            roles = source.Roles.OrderBy(r => r.UserId).ThenBy(r => r.BusinessId).ThenBy(r => r.RoleId).ThenBy(r => r.Enable),
            memberships = source.Memberships.OrderBy(t => t.UserId).ThenBy(t => t.BusinessId).ThenBy(t => t.Enable),
            walk_namespace = _options.WalkUserIdsUseProjectNamespace, walks = walks.Values.OrderBy(w => w.WalkId),
            institution_names = source.Institutions.OrderBy(t => t.Key)
        })))).ToLowerInvariant();
        var access = new PatientAccessContext(_options.IdentitySigningKey)
        {
            AnchorUserId = anchor.Id, Subject = proof.Subject, CurrentBusinessId = proof.CurrentBusinessId, DataScope = scope,
            UserIds = userIds, ScopeFingerprint = fingerprint, Institutions = source.Institutions,
            WalkUserIdsUseProjectNamespace = _options.WalkUserIdsUseProjectNamespace, WalkRecords = walks
        };
        foreach (var issue in issues) access.AddIssue(issue);
        return new(new TrustedPatientContext
        {
            PatientId = anchor.Id, SubjectRef = access.Reference("subject", proof.Subject),
            ConversationId = input.ConversationId ?? requestId, TraceId = requestId, Access = access
        });
    }

    private static bool Compatible(PatientEntity a, PatientEntity b)
    {
        static string Normalize(string? s) => (s ?? "").Trim().Normalize(NormalizationForm.FormKC);
        var nameA = Normalize(a.Name); var nameB = Normalize(b.Name);
        if (Regex.IsMatch(nameA + "|" + nameB, "测试|演示|test|demo", RegexOptions.IgnoreCase)) return false;
        var identityA = Normalize(a.IdentityId); var identityB = Normalize(b.IdentityId);
        if (identityA.Length > 0 && identityB.Length > 0 && identityA != identityB) return false;
        if (!(identityA.Length > 0 && identityA == identityB) && (nameA.Length == 0 || nameA != nameB)) return false;
        try
        {
            var profileA = JObject.Parse(a.UserDetails ?? "{}"); var profileB = JObject.Parse(b.UserDetails ?? "{}");
            foreach (var path in new[] { "patientInfo.birthDate", "patientInfo.birthday", "patientInfo.gender", "patientInfo.sex" })
            {
                var x = Normalize(profileA.SelectToken(path)?.ToString()); var y = Normalize(profileB.SelectToken(path)?.ToString());
                if (x.Length > 0 && y.Length > 0 && x != y) return false;
            }
        }
        catch (JsonException) { return false; }
        return true;
    }
}
