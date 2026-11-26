using System.Reflection;
using AI_WebApi.Controllers;
using AI_WebApi.IServices.AITools;
using AI_WebApi.Model.AITools;
using AI_WebApi.Model.Domain.IReGo;
using AI_WebApi.Model.DTOs.AITools;
using AI_WebApi.Model.Entities;
using AI_WebApi.Repository.AITools;
using AI_WebApi.Security;
using AI_WebApi.Services.AITools.IReGo;
using AI_WebApi.Services.AITools.PatientContext;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;

namespace AI_WebApi.Tests;

/// <summary>Pure contract/controller unit tests; no web server, HTTP probe or real database.</summary>
public sealed class PatientContractV2Tests
{
    private const string Key=PatientAccessContextTests.Key;

    [Fact]
    public void Patient_reference_is_shared_by_identity_and_IReGo_regardless_of_current_profile_institution()
    {
        var access=new PatientAccessContext(Key){ScopeFingerprint="confirmed-scope",AnchorUserId=7};
        var context=new TrustedPatientContext{PatientId=7,Access=access};
        var provider=new IReGoSessionRefProvider(Options.Create(new IReGoToolOptions{SessionRefSigningKey=Key}),
            new PatientAccessContextAccessor{Current=access});
        Assert.Equal(context.PatientRef,provider.CreatePatient(1,7));
        Assert.Equal(context.PatientRef,provider.CreatePatient(2,7));
    }

    [Fact]
    public async Task Cached_patient_context_preserves_partial_coverage_and_record_institution()
    {
        var access=new PatientAccessContext(Key){ScopeFingerprint="scope",AnchorUserId=7};
        var context=new TrustedPatientContext{PatientId=7,Access=access};
        var data=new MultisourcePatientContextDto{DataDomains=new PatientDataDomainsDto{Irego=new IReGoContextDomainDto
        {RecentSessions=new[]{new IReGoRecentSessionDto{SessionRef="record",RecordTenant=access.Tenant(null,"record")}}}}};
        var cache=new InMemoryPatientContextSnapshotCache();
        cache.Store(new PatientContextSnapshot{ContextKey=context.ContextKey,ExpiresAt=DateTimeOffset.UtcNow.AddMinutes(1),
            Data=data,ScopeIssues=new[]{"RECORD_TENANT_UNVERIFIED"}});
        var options=Options.Create(new IReGoToolOptions{SessionRefSigningKey=Key});
        var service=new MultisourcePatientContextService(new UnexpectedEvaluation(),new IReGoSessionRefProvider(options),cache,options);
        var result=await service.GetAsync(context,new(),"r");
        Assert.Equal("partial",access.Describe().Coverage);
        Assert.Contains("RECORD_TENANT_UNVERIFIED",access.Describe().Issues);
        Assert.Equal("unknown",result.Data!.DataDomains.Irego.RecentSessions[0].RecordTenant!.AttributionStatus);
    }

    private sealed class UnexpectedEvaluation : IIReGoPatientEvaluationService
    {
        public Task<IReGoPatientEvaluation> EvaluateAsync(TrustedPatientContext context,CancellationToken cancellationToken=default)
            => throw new InvalidOperationException("A populated snapshot should be used.");
    }

    [Fact]
    public void Patient_routes_have_one_contract_and_no_legacy_public_artifact_action()
    {
        var type=typeof(IReGoToolsController);
        var controller=new ControllerModel(type.GetTypeInfo(),type.GetCustomAttributes(true)){ControllerName="IReGoTools"};
        foreach(var method in type.GetMethods(BindingFlags.Instance|BindingFlags.Public|BindingFlags.DeclaredOnly))
            controller.Actions.Add(new ActionModel(method,method.GetCustomAttributes(true)){Controller=controller});
        var model=new ApplicationModel();model.Controllers.Add(controller);
        new PatientV2RoutingConvention().Apply(model);
        Assert.Single(model.Controllers);
        Assert.Equal("ai-patient-tools-v2",controller.ApiExplorer.GroupName);
        Assert.All(controller.Actions,a=>Assert.Contains(a.Attributes,x=>x is HttpPostAttribute));
        Assert.Contains(controller.Filters,f=>f is ServiceFilterAttribute s&&s.ServiceType==typeof(PatientV2ScopeFilter));
        Assert.Null(type.Assembly.GetType("AI_WebApi.Controllers.PatientIdentityMappingToolsController"));
        Assert.Null(type.Assembly.GetType("AI_WebApi.Controllers.AiPatientsController"));
    }

    [Fact]
    public void Numeric_user_alone_is_rejected_without_a_resolved_access_context()
    {
        var result=new DifySystemContextResolver().Resolve(new DefaultHttpContext(),new(){User="7"});
        Assert.False(result.IsSuccess);Assert.Equal(401,result.HttpStatusCode);
        Assert.Equal("PATIENT_IDENTITY_PROOF_REQUIRED",result.ReasonCode);
    }

    [Fact]
    public void Session_json_has_canonical_report_blocks_without_compatibility_aliases()
    {
        var json=JObject.FromObject(new IReGoSessionAnalysisDto());
        Assert.NotNull(json["report_blocks"]);
        Assert.Null(json["direct_training"]);Assert.Null(json["games"]);Assert.Null(json["assessments"]);
        Assert.Null(json["overview"]!["report_item_count"]);
        var evaluation=JObject.FromObject(new IReGoCapabilityEvaluationDto{BasicEvaluation="评价",PatientMessage="旧别名"});
        Assert.NotNull(evaluation["basic_evaluation"]);Assert.Null(evaluation["patient_message"]);
    }

    [Fact]
    public void Institution_segments_preserve_A_B_A_order_and_unknown_records()
    {
        var access=new PatientAccessContext(Key){AnchorUserId=7,Institutions=new Dictionary<int,string?>{{1,"A医院"},{2,"B卫生中心"}}};
        var records=new IReGoPatientHistoryDto{Items=new[]
        {
            Item("a1",1,access.Tenant(1,"record")),Item("b",2,access.Tenant(2,"record")),
            Item("a2",3,access.Tenant(1,"record")),Item("unknown",4,access.Tenant(null,"record"))
        }};
        var segments=RecordTenantPresentation.Segments(records,access);
        Assert.Equal(new[]{"A医院","B卫生中心","A医院","所属机构未确认"},segments.Select(s=>s.RecordTenant.DisplayName));
        Assert.All(segments,s=>Assert.Equal(1,s.RecordCount));
        Assert.Equal("verified",access.Tenant(999,"record").AttributionStatus);
        Assert.Equal("所属机构名称暂缺",access.Tenant(999,"record").DisplayName);
    }

    [Fact]
    public void Report_institution_annotation_is_html_encoded_and_uses_record_source()
    {
        var access=new PatientAccessContext(Key){CurrentBusinessId=2,Institutions=new Dictionary<int,string?>{{1,"A<script>医院"},{2,"B中心"}}};
        var data=new IReGoPatientHistoryDto{Items=new[]{Item("a",1,access.Tenant(1,"record"))}};
        var html=RecordTenantPresentation.AddToHtml("<html><h1>报告</h1></html>",data);
        Assert.Contains("所属机构",html);Assert.Contains("A&lt;script&gt;医院",html);Assert.DoesNotContain("A<script>",html);
        Assert.DoesNotContain("B中心",html);
    }

    [Fact]
    public void Identity_proofs_reject_tampering_expiry_wrong_purpose_and_excessive_lifetime()
    {
        var valid=PatientAccessContextTests.Proof();
        var token=PatientIdentityProofCodec.Issue(valid,Key);
        Assert.NotNull(PatientIdentityProofCodec.Verify(token,Key,DateTimeOffset.UtcNow,900));
        Assert.Null(PatientIdentityProofCodec.Verify(token+"x",Key,DateTimeOffset.UtcNow,900));
        Assert.Null(PatientIdentityProofCodec.Verify(token,Key,DateTimeOffset.UtcNow.AddMinutes(15),900));
        var excessive=new PatientIdentityProof{Subject="s",ActorRole="patient",AnchorUserId=7,IssuedAt=valid.IssuedAt,ExpiresAt=valid.IssuedAt+901};
        Assert.Null(PatientIdentityProofCodec.Verify(PatientIdentityProofCodec.Issue(excessive,Key),Key,DateTimeOffset.UtcNow,900));
    }

    [Fact]
    public async Task Report_grant_rechecks_current_bindings_and_is_not_a_general_identity_credential()
    {
        using var db=new PatientAccessTestDatabase();
        var options=new PatientAccessOptions{IdentitySigningKey=Key};
        var resolver=new PatientAccessContextResolver(new PatientAccessRepository(db),Options.Create(options));
        var proof=PatientAccessContextTests.Proof();
        var resolved=await resolver.ResolveAsync(PatientAccessContextTests.Input(proof),null,"r",default);
        var images=new IReGoToolOptions();images.ReportImage.SigningKey=Key;
        var store=new EphemeralIReGoReportArtifactStore(Options.Create(images));
        var reference=store.CreateArtifactRef(resolved.Context!.PatientRef,"session","snapshot","template");
        Assert.StartsWith("irego_scope_v2_",reference);
        store.Store(reference,new byte[]{1,2,3});
        var grant=PatientArtifactGrantCodec.Issue(new(reference,resolved.Context.Access!.ScopeFingerprint,"all_tenants",proof,
            DateTimeOffset.UtcNow.AddMinutes(2).ToUnixTimeSeconds()),Key);
        Assert.Null(PatientIdentityProofCodec.Verify(grant,Key,DateTimeOffset.UtcNow,900));
        var controller=new PatientReportArtifactsV2Controller(resolver,store,Options.Create(options))
        {ControllerContext=new ControllerContext{HttpContext=new DefaultHttpContext()}};
        Assert.IsType<FileContentResult>(await controller.Content(reference,grant,default));
        db.Db.Updateable<PatientTenantEntity>().SetColumns(t=>t.Enable==-1).Where(t=>t.UserId==7).ExecuteCommand();
        var changed=Assert.IsType<StatusCodeResult>(await controller.Content(reference,grant,default));
        Assert.Equal(403,changed.StatusCode);
        Assert.IsType<UnauthorizedResult>(await controller.Content(reference,grant+"x",default));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task History_reads_the_original_bytes_by_reference_after_the_download_URL_expires(bool requireProof)
    {
        using var db = new PatientAccessTestDatabase();
        var options = Options.Create(new PatientAccessOptions { IdentitySigningKey = Key, RequireIdentityProof = requireProof });
        var resolver = new PatientAccessContextResolver(new PatientAccessRepository(db), options);
        var proof = PatientAccessContextTests.Proof();
        var current = PatientAccessContextTests.Input(proof);
        var original = await resolver.ResolveAsync(current, "all_tenants", "test", default);
        var images = Options.Create(new IReGoToolOptions { DifyServiceBearerToken = "history-test-service" });
        images.Value.ReportImage.SigningKey = Key;
        images.Value.ReportImage.PublicBaseUrl = "http://localhost:15043";
        var store = new EphemeralIReGoReportArtifactStore(images);
        var reference = store.CreateArtifactRef(original.Context!.PatientRef, "session", "snapshot", "template");
        var bytes = new byte[] { 3, 4, 5 };
        store.Store(reference, bytes);
        var receipt = PatientArtifactGrantCodec.Issue(new(reference, original.Context.Access!.ScopeFingerprint,
            "all_tenants", proof, DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeSeconds()), Key);
        var api = new PatientReportArtifactsV2Controller(resolver, store, options)
            { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
        var auth = new DifyServiceRequestAuthenticator(images);
        var ownerOnly = new DifySystemContextDto { User = proof.Subject };
        Task<IActionResult> Read(DifySystemContextDto viewer, string saved, string id) => api.ReadForChat(id,
            new() { SystemContext = viewer, OriginalGrant = saved }, auth, default);
        Assert.IsType<UnauthorizedResult>(await api.Content(reference, receipt, default));
        Assert.Equal(401, Assert.IsType<StatusCodeResult>(await Read(current, receipt, reference)).StatusCode);
        api.Request.Headers.Authorization = "Bearer history-test-service";
        Assert.Equal(403, Assert.IsType<StatusCodeResult>(await Read(new() { User = "other" }, receipt, reference)).StatusCode);
        Assert.Equal(403, Assert.IsType<StatusCodeResult>(await Read(current, receipt + "x", reference)).StatusCode);
        Assert.Equal(403, Assert.IsType<StatusCodeResult>(await Read(current, receipt, reference + "0")).StatusCode);
        if (requireProof) Assert.Equal(401, Assert.IsType<StatusCodeResult>(await Read(ownerOnly, receipt, reference)).StatusCode);
        var viewer = requireProof ? current : ownerOnly;
        var read = Assert.IsType<FileContentResult>(await Read(viewer, receipt, reference));
        Assert.Equal("image/png", read.ContentType);
        Assert.Equal(bytes, read.FileContents);
        db.Db.Updateable<PatientTenantEntity>().SetColumns(t => t.Enable == -1).Where(t => t.UserId == 7).ExecuteCommand();
        Assert.Equal(403, Assert.IsType<StatusCodeResult>(await Read(viewer, receipt, reference)).StatusCode);
    }

    private static IReGoHistoryItemDto Item(string reference,int day,RecordTenantDto tenant)=>new()
    {SessionRef=reference,SessionTime=new DateTimeOffset(2026,10,day,9,0,0,TimeSpan.FromHours(8)),RecordTenant=tenant};
}
