using AI_WebApi.IServices.AITools;
using AI_WebApi.Model.AITools;
using AI_WebApi.Model.Domain.IReGo;
using AI_WebApi.Model.DTOs.AITools;
using AI_WebApi.Model.Entities;
using AI_WebApi.Repository.AITools;
using AI_WebApi.Services.AITools.IReGo;
using AI_WebApi.Services.AITools.PatientContext;
using Microsoft.Extensions.Options;

namespace AI_WebApi.Tests;

public sealed class PatientAccessContextTests
{
    internal const string Key = "synthetic-patient-access-signing-key-20261029";
    internal static PatientIdentityProof Proof(int owner=7,int? tenant=1,bool phone=true) => new()
    {
        Subject="synthetic-subject",AnchorUserId=owner,ActorRole="patient",CurrentBusinessId=tenant,AllowAllTenants=true,
        PhoneBindingHash=phone ? PatientIdentityProofCodec.PhoneBindingHash("13800000001",Key) : null,
        IssuedAt=DateTimeOffset.UtcNow.ToUnixTimeSeconds(),ExpiresAt=DateTimeOffset.UtcNow.AddMinutes(10).ToUnixTimeSeconds()
    };
    internal static DifySystemContextDto Input(PatientIdentityProof? proof=null,string kind="project_user_id",string value="7")
    {
        proof ??= Proof();
        return new() { User=proof.Subject,PatientIdentity=new() {Type=kind,Value=value},CurrentBusinessId=proof.CurrentBusinessId,
            IdentityContextToken=PatientIdentityProofCodec.Issue(proof,Key) };
    }
    private static PatientAccessContextResolver Resolver(PatientAccessTestDatabase db,PatientAccessOptions? options=null) =>
        new(new PatientAccessRepository(db),Options.Create(options ?? new PatientAccessOptions { IdentitySigningKey=Key }));

    [Fact]
    public async Task All_tenants_resolves_same_patient_and_preserves_unknown_institution_records()
    {
        using var db=new PatientAccessTestDatabase();
        var resolved=await Resolver(db).ResolveAsync(Input(),null,"request",default);
        Assert.True(resolved.IsSuccess,resolved.ReasonCode);
        Assert.Equal(new[]{7,8},resolved.Context!.Access!.UserIds);
        var source=await new IReGoDataRepository(db).LoadPatientDataAsync(resolved.Context);
        Assert.Equal(new[]{11,12,13,15},source.Plans.Select(p=>p.Id).OrderBy(x=>x));
        Assert.Equal("A医院",source.Plans.Single(p=>p.Id==11).RecordTenant!.DisplayName);
        Assert.Equal("B卫生服务中心",source.Plans.Single(p=>p.Id==13).RecordTenant!.DisplayName);
        Assert.Equal("unknown",source.Plans.Single(p=>p.Id==15).RecordTenant!.AttributionStatus);
        Assert.Equal(2,resolved.Context.Access.Describe().IncludedTenantCount);
    }

    [Theory]
    [InlineData(1,new[]{11})]
    [InlineData(2,new[]{12,13})]
    public async Task Current_tenant_filters_records_not_only_user_memberships(int tenant,int[] expected)
    {
        using var db=new PatientAccessTestDatabase();
        var result=await Resolver(db).ResolveAsync(Input(Proof(tenant:tenant)),"current_tenant","r",default);
        var source=await new IReGoDataRepository(db).LoadPatientDataAsync(result.Context!);
        Assert.Equal(expected,source.Plans.Select(p=>p.Id).OrderBy(x=>x));
    }

    [Fact]
    public async Task Global_pagination_and_counts_do_not_expand_from_duplicate_membership_rows()
    {
        using var db=new PatientAccessTestDatabase();
        var result=await Resolver(db).ResolveAsync(Input(),null,"r",default);
        var repository=new PatientLayerRepository(db);
        var first=await repository.PlansAsync(result.Context!,1,2,default);
        var second=await repository.PlansAsync(result.Context!,2,2,default);
        Assert.Equal(5,first.Count); Assert.Equal(5,second.Count);
        Assert.Equal(new[]{16,15},first.Rows.Select(x=>x.PlanId));
        Assert.Equal(new[]{13,12},second.Rows.Select(x=>x.PlanId));
    }

    [Fact]
    public async Task Names_conflicting_with_same_phone_do_not_grant_cross_patient_access()
    {
        using var db=new PatientAccessTestDatabase();
        db.Db.Updateable<PatientEntity>().SetColumns(p=>p.Name=="其他患者").Where(p=>p.Id==8).ExecuteCommand();
        var result=await Resolver(db).ResolveAsync(Input(),null,"r",default);
        Assert.Equal(new[]{7},result.Context!.Access!.UserIds);
        Assert.Contains("IDENTITY_LINK_AMBIGUOUS",result.Context.Access.Describe().Issues);
    }

    [Fact]
    public async Task Login_name_keeps_leading_zero_and_duplicate_login_fails_closed()
    {
        using var db=new PatientAccessTestDatabase();
        var resolver=Resolver(db);
        Assert.True((await resolver.ResolveAsync(Input(kind:"login_name",value:"000007"),null,"r",default)).IsSuccess);
        db.Db.Updateable<PatientEntity>().SetColumns(p=>p.LoginName=="000007").Where(p=>p.Id==8).ExecuteCommand();
        var duplicate=await resolver.ResolveAsync(Input(kind:"login_name",value:"000007"),null,"r",default);
        Assert.Equal("LOGIN_NAME_NOT_UNIQUE",duplicate.ReasonCode);
        Assert.False((await resolver.ResolveAsync(Input(kind:"project_user_id",value:"000007"),null,"r",default)).Context==null);
    }

    [Fact]
    public async Task Signed_proof_is_required_and_cannot_be_replayed_for_another_anchor()
    {
        using var db=new PatientAccessTestDatabase();
        var resolver=Resolver(db);
        Assert.Equal(401,(await resolver.ResolveAsync(new(){User="7",PatientIdentity=new(){Type="project_user_id",Value="7"}},null,"r",default)).HttpStatusCode);
        Assert.Equal("PATIENT_IDENTITY_MISMATCH",(await resolver.ResolveAsync(Input(value:"8"),null,"r",default)).ReasonCode);
        Assert.Equal("CURRENT_TENANT_ACCESS_DENIED",(await resolver.ResolveAsync(Input(Proof(tenant:99)),"current_tenant","r",default)).ReasonCode);
    }

    [Fact]
    public async Task Doctor_role_and_unverified_phone_cannot_expand_patient_ownership()
    {
        using var db=new PatientAccessTestDatabase();
        var resolver=Resolver(db);
        var noPhone=await resolver.ResolveAsync(Input(Proof(phone:false)),null,"r",default);
        Assert.Equal(new[]{7},noPhone.Context!.Access!.UserIds);
        db.Db.Updateable<DbRoleUserEntity>().SetColumns(r=>r.RoleId==1).Where(r=>r.UserId==7).ExecuteCommand();
        Assert.Equal("PATIENT_SELF_ACCESS_REQUIRED",(await resolver.ResolveAsync(Input(),null,"r",default)).ReasonCode);
    }

    [Fact]
    public async Task Verified_historical_aliases_survive_disabled_login_but_arbitrary_same_phone_does_not()
    {
        using var db=new PatientAccessTestDatabase();
        db.Db.Updateable<PatientEntity>().SetColumns(p=>p.Status==9).Where(p=>p.Id==8).ExecuteCommand();
        Assert.Equal(new[]{7},(await Resolver(db).ResolveAsync(Input(),null,"r",default)).Context!.Access!.UserIds);
        var options=new PatientAccessOptions { IdentitySigningKey=Key,ConfirmedAccountGroups=new(){new[]{7,8}} };
        Assert.Equal(new[]{7,8},(await Resolver(db,options).ResolveAsync(Input(),null,"r",default)).Context!.Access!.UserIds);
    }

    [Fact]
    public async Task Scope_and_membership_changes_invalidate_references_and_cached_context_keys()
    {
        using var db=new PatientAccessTestDatabase(); var resolver=Resolver(db);
        var all=(await resolver.ResolveAsync(Input(),null,"r",default)).Context!;
        var current=(await resolver.ResolveAsync(Input(),"current_tenant","r",default)).Context!;
        var accessor=new PatientAccessContextAccessor { Current=all.Access };
        var refs=new IReGoSessionRefProvider(Options.Create(new IReGoToolOptions {SessionRefSigningKey=Key}),accessor);
        var reference=refs.Create(1,7,11,21,"schema");
        accessor.Current=current.Access;
        Assert.False(refs.Matches(reference,1,7,11,21,"schema"));
        Assert.NotEqual(all.ContextKey,current.ContextKey);
        db.Db.Updateable<PatientTenantEntity>().SetColumns(t=>t.Enable==-1).Where(t=>t.UserId==8).ExecuteCommand();
        var changed=(await resolver.ResolveAsync(Input(),null,"r",default)).Context!;
        Assert.NotEqual(all.Access!.ScopeFingerprint,changed.Access!.ScopeFingerprint);
    }
}
