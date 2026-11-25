using System.Security.Cryptography;
using System.Text;
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
    private readonly IDoctorDelegationValidator? _delegations;
    public PatientAccessContextResolver(IPatientAccessRepository repository, IOptions<PatientAccessOptions> options,
        IDoctorDelegationValidator? delegations = null)
    { _repository = repository; _options = options.Value; _delegations = delegations; }

    public async Task<PatientAccessResolution> ResolveAsync(DifySystemContextDto? input, string? dataScope, string requestId, CancellationToken ct)
    {
        static PatientAccessResolution Deny(string reason, int status = 401) => new(null, reason, status);
        if (Encoding.UTF8.GetByteCount(_options.IdentitySigningKey) < 32) return Deny("PATIENT_IDENTITY_SIGNING_NOT_CONFIGURED", 503);
        var scope = dataScope ?? "all_tenants";
        if (scope is not ("all_tenants" or "current_tenant")) return Deny("INVALID_DATA_SCOPE", 400);
        var hasToken = !string.IsNullOrWhiteSpace(input?.IdentityContextToken);
        PatientIdentityProof? proof = null;
        if (_options.RequireIdentityProof || hasToken)
        {
            proof = PatientIdentityProofCodec.VerifyForAccess(input?.IdentityContextToken, _options, DateTimeOffset.UtcNow);
            if (proof == null || input?.User != proof.Subject) return Deny("PATIENT_IDENTITY_PROOF_INVALID");
            if (proof.ActorRole == "doctor" && (scope != "current_tenant" || _delegations == null ||
                !await _delegations.ValidateAsync(proof.DoctorDelegation!, ct))) return Deny("DOCTOR_DELEGATION_DENIED", 403);
            if (scope == "all_tenants" && !proof.AllowAllTenants) return Deny("PATIENT_SCOPE_FORBIDDEN", 403);
            if (input.CurrentBusinessId.HasValue && input.CurrentBusinessId != proof.CurrentBusinessId) return Deny("CURRENT_TENANT_MISMATCH", 403);
        }
        else if (input == null || string.IsNullOrWhiteSpace(input.User) || input.User.Length > 256)
        {
            return Deny("INVALID_DIFY_SYSTEM_CONTEXT", 400);
        }
        if (input!.ConversationId?.Length > 256) return Deny("INVALID_DIFY_SYSTEM_CONTEXT", 400);
        var identity = input.PatientIdentity;
        if (identity == null || identity.Type is not ("project_user_id" or "login_name") ||
            string.IsNullOrWhiteSpace(identity.Value) || identity.Value.Length > 64 || identity.Value != identity.Value.Trim())
            return Deny("INVALID_PATIENT_IDENTITY", 400);
        var anchors = await _repository.FindAnchorAsync(identity.Type, identity.Value, ct);
        var matchedByPhone = false;
        if (anchors.Count == 0 && !_options.RequireIdentityProof && identity.Type == "login_name")
        {
            anchors = await _repository.FindAnchorByPhoneAsync(identity.Value, ct);
            matchedByPhone = anchors.Count > 0;
        }
        if (anchors.Count != 1)
        {
            if (anchors.Count > 1)
                return Deny(matchedByPhone ? "PHONE_NOT_UNIQUE" : (identity.Type == "login_name" ? "LOGIN_NAME_NOT_UNIQUE" : "PATIENT_IDENTITY_NOT_FOUND"), 404);
            return Deny("PATIENT_IDENTITY_NOT_FOUND", 404);
        }
        var anchor = anchors[0];
        if (proof == null)
        {
            var nowSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var lifetime = Math.Clamp(_options.MaximumProofLifetimeSeconds, 30, 900);
            proof = new PatientIdentityProof
            {
                Purpose = "patient_rehab_v2",
                Subject = input.User.Trim(),
                AnchorUserId = anchor.Id,
                ActorRole = "patient",
                CurrentBusinessId = input.CurrentBusinessId,
                AllowAllTenants = true,
                IssuedAt = nowSeconds,
                ExpiresAt = nowSeconds + lifetime
            };
        }
        var doctor = proof.ActorRole == "doctor";
        if (anchor.Id != proof.AnchorUserId) return Deny("PATIENT_IDENTITY_MISMATCH", 403);
        if (anchor.Status != 1 || anchor.IsDeleted != false) return Deny("PATIENT_ACCOUNT_UNAVAILABLE", 403);
        if (anchor.BusinessId != 0) return Deny("PATIENT_GLOBAL_ACCOUNT_REQUIRED", 403);
        var source = await _repository.LoadAccessAsync(anchor.Id, ct);
        var selfRoles = source.Roles.Where(role => role.UserId == anchor.Id && role.Enable == 0 &&
            role.RoleId is DbRoleUserEntity.PatientRoleId or DbRoleUserEntity.StudentRoleId);
        if (!selfRoles.Any()) return Deny("PATIENT_SELF_ACCESS_REQUIRED", 403);
        if (scope == "current_tenant" && (proof.CurrentBusinessId is not > 0 ||
            !source.Memberships.Any(t => t.UserId == anchor.Id && t.BusinessId == proof.CurrentBusinessId && t.Enable == 0) ||
            !selfRoles.Any(role => role.BusinessId == proof.CurrentBusinessId)))
            return Deny("CURRENT_TENANT_ACCESS_DENIED", 403);

        var scopedRoles = selfRoles.Where(role => scope == "all_tenants" || role.BusinessId == proof.CurrentBusinessId);
        if (proof.ActorRole == "student" && !scopedRoles.Any(role => role.RoleId == DbRoleUserEntity.StudentRoleId))
            return Deny("PATIENT_SELF_ACCESS_REQUIRED", 403);
        var actorRole = proof.ActorRole == "patient" && !scopedRoles.Any(role => role.RoleId == DbRoleUserEntity.PatientRoleId)
            ? "student" : proof.ActorRole;
        var scopeMaterial = JObject.FromObject(new
        {
            version = "global-patient-account-v1", proof.Subject, anchor.Id, actorRole, scope,
            tenant = scope == "current_tenant" ? proof.CurrentBusinessId : null,
            roles = source.Roles.OrderBy(r => r.UserId).ThenBy(r => r.BusinessId).ThenBy(r => r.RoleId).ThenBy(r => r.Enable),
            memberships = source.Memberships.OrderBy(t => t.UserId).ThenBy(t => t.BusinessId).ThenBy(t => t.Enable),
            institution_names = source.Institutions.OrderBy(t => t.Key)
        });
        if (doctor) scopeMaterial["DoctorDelegation"] = JObject.FromObject(proof.DoctorDelegation!);
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(scopeMaterial.ToString(Formatting.None)))).ToLowerInvariant();
        var access = new PatientAccessContext(_options.IdentitySigningKey)
        {
            AnchorUserId = anchor.Id, Subject = proof.Subject, ActorRole = actorRole, CurrentBusinessId = proof.CurrentBusinessId, DataScope = scope,
            ScopeFingerprint = fingerprint, Institutions = source.Institutions
        };
        return new(new TrustedPatientContext
        {
            PatientId = anchor.Id, SubjectRef = access.Reference("subject", proof.Subject),
            ConversationId = input.ConversationId ?? requestId, TraceId = requestId, Access = access
        });
    }
}
