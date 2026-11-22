using AI_WebApi.Controllers;
using AI_WebApi.IServices.AITools;
using AI_WebApi.Model.AITools;
using AI_WebApi.Model.DTOs.AITools;
using AI_WebApi.Repository.AITools;
using AI_WebApi.Services.AITools.IReGo;
using AI_WebApi.Services.AITools.PatientContext;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace AI_WebApi.Tests;

public sealed class DoctorDelegationV2Tests
{
    private const string DoctorKey = "synthetic-independent-doctor-signing-key-20261103";
    private static PatientAccessOptions Config() => new()
    {
        IdentitySigningKey = PatientAccessContextTests.Key, DoctorDelegationSigningKey = DoctorKey
    };
    private static PatientIdentityProof Proof(int version = 1) => new()
    {
        Purpose = "doctor_rehab_v2", ActorRole = "doctor", AnchorUserId = 7, CurrentBusinessId = 1,
        Subject = "doctor-workspace:synthetic:doctor", IssuedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(4).ToUnixTimeSeconds(),
        DoctorDelegation = new() { PatientId = 7, InstitutionId = 1, DoctorId = 10,
            AccountId = "doctor", Workspace = "synthetic", RelationshipRef = new string('a', 32), RelationshipVersion = version }
    };
    private static DifySystemContextDto Input(PatientIdentityProof? proof = null)
    {
        proof ??= Proof();
        return new() { User = proof.Subject, PatientIdentity = new() { Type = "project_user_id", Value = "7" },
            CurrentBusinessId = 1, IdentityContextToken = PatientIdentityProofCodec.Issue(proof, DoctorKey) };
    }
    private sealed class Validation : IDoctorDelegationValidator
    {
        public bool Active = true;
        public int Calls;
        public Task<bool> ValidateAsync(DoctorDelegationDto grant, CancellationToken ct)
        { Calls++; return Task.FromResult(Active); }
    }

    [Fact]
    public void Doctor_proofs_require_independent_key_purpose_and_prefix()
    {
        var token = Input().IdentityContextToken;
        Assert.NotNull(PatientIdentityProofCodec.VerifyForAccess(token, Config(), DateTimeOffset.UtcNow));
        Assert.Null(PatientIdentityProofCodec.Verify(token, DoctorKey, DateTimeOffset.UtcNow, 900));
        var wrongKey = PatientIdentityProofCodec.Issue(Proof(), PatientAccessContextTests.Key);
        Assert.Null(PatientIdentityProofCodec.VerifyForAccess(wrongKey, Config(), DateTimeOffset.UtcNow));
        Assert.Null(PatientIdentityProofCodec.VerifyForAccess(token + "x", Config(), DateTimeOffset.UtcNow));
        Assert.Null(PatientIdentityProofCodec.VerifyForAccess(token, Config(), DateTimeOffset.UtcNow.AddMinutes(5)));
    }

    [Fact]
    public async Task Doctor_scope_includes_only_authorized_account_and_current_institution()
    {
        using var db = new PatientAccessTestDatabase();
        var validation = new Validation();
        var resolver = new PatientAccessContextResolver(new PatientAccessRepository(db), Options.Create(Config()), validation);
        var resolved = await resolver.ResolveAsync(Input(), "current_tenant", "r", default);
        Assert.True(resolved.IsSuccess, resolved.ReasonCode);
        Assert.Equal(7, resolved.Context!.Access!.AnchorUserId);
        Assert.Equal("doctor", resolved.Context.Access.ActorRole);
        var data = await new IReGoDataRepository(db).LoadPatientDataAsync(resolved.Context);
        Assert.Equal(new[] { 11 }, data.Plans.Select(p => p.Id));
        Assert.Equal(21, Assert.Single(data.Reports).Id);
        Assert.Equal(1, validation.Calls);
        Assert.Equal("DOCTOR_DELEGATION_DENIED", (await resolver.ResolveAsync(Input(), "all_tenants", "r", default)).ReasonCode);
        validation.Active = false;
        Assert.Equal("DOCTOR_DELEGATION_DENIED", (await resolver.ResolveAsync(Input(), "current_tenant", "r", default)).ReasonCode);
        Assert.Equal(2, validation.Calls);
    }

    [Fact]
    public async Task Missing_validation_and_relationship_version_changes_fail_closed()
    {
        using var db = new PatientAccessTestDatabase();
        var repository = new PatientAccessRepository(db);
        Assert.False((await new PatientAccessContextResolver(repository, Options.Create(Config()))
            .ResolveAsync(Input(), "current_tenant", "r", default)).IsSuccess);
        var resolver = new PatientAccessContextResolver(repository, Options.Create(Config()), new Validation());
        var first = await resolver.ResolveAsync(Input(), "current_tenant", "r", default);
        var second = await resolver.ResolveAsync(Input(Proof(2)), "current_tenant", "r", default);
        Assert.NotEqual(first.Context!.Access!.ScopeFingerprint, second.Context!.Access!.ScopeFingerprint);
    }

    [Fact]
    public async Task Artifact_download_rechecks_doctor_authorization_and_revocation()
    {
        using var db = new PatientAccessTestDatabase();
        var validation = new Validation();
        var options = Config();
        var resolver = new PatientAccessContextResolver(new PatientAccessRepository(db), Options.Create(options), validation);
        var resolved = await resolver.ResolveAsync(Input(), "current_tenant", "r", default);
        var imageOptions = new IReGoToolOptions(); imageOptions.ReportImage.SigningKey = PatientAccessContextTests.Key;
        var store = new EphemeralIReGoReportArtifactStore(Options.Create(imageOptions));
        var reference = store.CreateArtifactRef(resolved.Context!.PatientRef, "session", "snapshot", "template");
        store.Store(reference, new byte[] { 1, 2, 3 });
        var grant = new PatientArtifactGrant(reference, resolved.Context.Access!.ScopeFingerprint, "current_tenant", Proof(), DateTimeOffset.UtcNow.AddMinutes(3).ToUnixTimeSeconds());
        var controller = new PatientReportArtifactsV2Controller(resolver, store, Options.Create(options))
        { ControllerContext = new() { HttpContext = new DefaultHttpContext() } };
        var token = PatientArtifactGrantCodec.Issue(grant, options.IdentitySigningKey);
        Assert.IsType<FileContentResult>(await controller.Content(reference, token, default));
        validation.Active = false;
        Assert.Equal(403, Assert.IsType<StatusCodeResult>(await controller.Content(reference, token, default)).StatusCode);
    }
}
