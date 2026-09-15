using System.Security.Cryptography;
using System.Text;
using AI_WebApi.Controllers;
using AI_WebApi.IServices.AITools;
using AI_WebApi.Model.AITools;
using AI_WebApi.Model.Domain.IReGo;
using AI_WebApi.Model.DTOs.AITools;
using AI_WebApi.Services.AITools.IReGo;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AI_WebApi.Tests;

public sealed class ProjectMigrationTests
{
    private const string Key = "unit-test-only-persistent-signing-key-32-bytes";
    private static readonly string LegacySession = "irego_s_v1_" + new string('a', 32);
    private static readonly string CurrentSession = IReGoSessionRefProviderTests.Provider().Create(3, 7, 11, 11, "schema");
    private static readonly TrustedPatientContext Context = new() { PatientId = 7, SubjectRef = "subject:7" };

    [Fact]
    public void Correctly_signed_old_session_is_rejected_even_with_same_key()
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(Key));
        var old = "irego_s_v1_" + Convert.ToHexString(hmac.ComputeHash(
            Encoding.UTF8.GetBytes("v1|robotdb_main|3|7|11|11|schema")).AsSpan(0, 16)).ToLowerInvariant();
        var provider = IReGoSessionRefProviderTests.Provider();
        Assert.False(provider.Matches(old, 3, 7, 11, 11, "schema"));
        Assert.False(provider.Matches(old.Replace("_v1_", "_v2_"), 3, 7, 11, 11, "schema"));
        Assert.True(provider.Matches(CurrentSession, 3, 7, 11, 11, "schema"));
        Assert.False(provider.Matches(CurrentSession, 4, 7, 11, 11, "schema"));
        Assert.False(provider.Matches(CurrentSession, 3, 7, 12, 11, "schema"));
        Assert.False(provider.Matches(CurrentSession, 3, 7, 11, 11, "different"));
        Assert.NotEqual(provider.CreatePatient(3, 7), provider.CreatePlan(3, 7, 11));
    }

    [Theory]
    [InlineData("session_ref")]
    [InlineData("latest")]
    public async Task Retired_session_is_reported_before_database_or_cache_access(string selector)
    {
        var service = new IReGoSessionAnalysisService(new NeverEvaluate(),
            IReGoSessionRefProviderTests.Provider(), new InMemoryPatientContextSnapshotCache(), null!, Settings());
        var response = await service.GetAsync(Context, new GetIReGoSessionAnalysisRequestDto
        { Selector = selector, SessionRef = LegacySession }, "request");
        Assert.Equal(ToolStatuses.Failed, response.Status);
        Assert.Null(response.Data);
        Assert.Contains(IReGoReasonCodes.SessionRefNotFound, response.ReasonCodes);
        Assert.Equal(IReGoDataVersion.RetiredSessionMessage, response.PatientMessage);
    }

    [Fact]
    public async Task Mixed_longitudinal_references_fail_before_window_or_database_evaluation()
    {
        var service = new IReGoLongitudinalAnalysisService(new NeverEvaluate(),
            IReGoSessionRefProviderTests.Provider(), null!, Settings());
        var response = await service.GetAsync(Context, new GetIReGoLongitudinalAnalysisRequestDto
        { Selector = "session_refs", SessionRefs = new[] { CurrentSession, LegacySession } }, "request");
        Assert.Contains(IReGoReasonCodes.SessionRefNotFound, response.ReasonCodes);
        Assert.Equal(IReGoDataVersion.RetiredSessionMessage, response.PatientMessage);
    }

    [Fact]
    public void Cache_rejects_old_keys_old_bindings_and_relabelled_old_snapshot_contents()
    {
        var cache = new InMemoryPatientContextSnapshotCache();
        var oldKey = "subject:7|patient:7";
        cache.Store(Snapshot(oldKey));
        Assert.False(cache.TryGetSnapshot(oldKey, out _));
        cache.StoreSession(new IReGoSessionBinding { ContextKey = Context.ContextKey,
            SessionRef = LegacySession, ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5) });
        Assert.False(cache.TryResolveSession(Context.ContextKey, LegacySession, out _));
        cache.Store(new PatientContextSnapshot { ContextKey = Context.ContextKey,
            SnapshotRef = "ctx_v2_" + new string('1', 32), ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5),
            Data = new MultisourcePatientContextDto { PatientRef = "irego_patient_v1_old" } });
        Assert.False(cache.TryGetSnapshot(Context.ContextKey, out _));
        cache.Store(Snapshot(Context.ContextKey));
        Assert.True(cache.TryGetSnapshot(Context.ContextKey, out _));
        Assert.False(new InMemoryPatientContextSnapshotCache().TryGetSnapshot(Context.ContextKey, out _));
        Assert.True(IReGoSessionRefProviderTests.Provider().Matches(CurrentSession, 3, 7, 11, 11, "schema"));
    }

    [Fact]
    public void Same_session_binding_in_different_contexts_does_not_overwrite()
    {
        var cache = new InMemoryPatientContextSnapshotCache();
        var otherKey = new TrustedPatientContext { PatientId = 7, SubjectRef = "another-subject" }.ContextKey;
        foreach (var key in new[] { Context.ContextKey, otherKey })
            cache.StoreSession(new IReGoSessionBinding { ContextKey = key, SessionRef = CurrentSession,
                ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5) });
        Assert.True(cache.TryResolveSession(Context.ContextKey, CurrentSession, out _));
        Assert.True(cache.TryResolveSession(otherKey, CurrentSession, out _));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Both_stores_reject_old_artifacts_and_signed_old_tokens(bool durable)
    {
        var path = Path.Combine(Path.GetTempPath(), "irego-migration-" + Guid.NewGuid().ToString("N"));
        var settings = Settings(path);
        IIReGoReportArtifactStore store = durable
            ? new FileSystemIReGoReportArtifactStore(settings)
            : new EphemeralIReGoReportArtifactStore(settings);
        try
        {
            foreach (var old in new[] { "irego_img_v1_" + new string('a', 32), "irego_img_v2_" + new string('a', 64) })
            {
                if (durable)
                    File.WriteAllBytes(Path.Combine(path, IReGoDataVersion.StorageDirectory, old + ".png"), new byte[] { 1 });
                Assert.False(store.TryGet(old, out _));
                Assert.Equal(ReportImageAccessStatus.Invalid, store.ResolveArtifact(old).Status);
                Assert.Throws<InvalidOperationException>(() => store.Store(old, new byte[] { 1 }));
                Assert.Throws<InvalidOperationException>(() => store.IssueAccessToken(old, DateTimeOffset.UtcNow.AddMinutes(1)));
                foreach (var expiry in new[] { DateTimeOffset.UtcNow.AddMinutes(5), DateTimeOffset.UtcNow.AddMinutes(-5) })
                {
                    var payload = old + "|" + expiry.ToUnixTimeSeconds();
                    using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(Key));
                    var token = "v1." + Base64(Encoding.UTF8.GetBytes(payload)) + "." +
                        Base64(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload)).AsSpan(0, 16).ToArray());
                    var result = store.Resolve(token);
                    Assert.Equal(ReportImageAccessStatus.Invalid, result.Status);
                    Assert.Empty(result.ArtifactRef); // Cannot recover via expired-token redirect.
                }
            }
            var current = store.CreateArtifactRef("irego_patient_v2_current", CurrentSession, "content", "template");
            store.Store(current, new byte[] { 1, 2 });
            var newToken = store.IssueAccessToken(current, DateTimeOffset.UtcNow.AddMinutes(1));
            Assert.StartsWith("v2.", newToken);
            Assert.Equal(ReportImageAccessStatus.Available, store.Resolve(newToken).Status);
            Assert.Equal(ReportImageAccessStatus.Invalid, store.Resolve(newToken.Replace("v2.", "v1.")).Status);
        }
        finally { if (Directory.Exists(path)) Directory.Delete(path, true); }
    }

    [Theory]
    [InlineData("irego_img_v1_aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("irego_img_v2_aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("v1.old.signed")]
    public void Old_http_links_return_404_notice_without_reading_artifact_store(string reference)
    {
        var controller = Controller();
        var action = reference.StartsWith("v1.")
            ? controller.GetIReGoReportImage(reference) : controller.GetIReGoReportArtifact(reference);
        var content = Assert.IsType<ContentResult>(action);
        Assert.Equal(404, content.StatusCode);
        Assert.Equal(IReGoDataVersion.RetiredReportMessage, content.Content);
        Assert.Contains("no-store", controller.Response.Headers["Cache-Control"].ToString());
    }

    [Fact]
    public async Task Public_longitudinal_lists_remain_forbidden_with_clear_retirement_notice()
    {
        var response = await Controller().GetIReGoLongitudinalAnalysis(
            new GetIReGoLongitudinalAnalysisRequestDto { SessionRefs = new[] { LegacySession } }, default);
        var bad = Assert.IsType<BadRequestObjectResult>(response.Result);
        var envelope = Assert.IsType<ToolResponseDto<IReGoLongitudinalAnalysisDto>>(bad.Value);
        Assert.Contains(IReGoReasonCodes.InvalidRequest, envelope.ReasonCodes);
        Assert.Equal(IReGoDataVersion.RetiredSessionMessage, envelope.PatientMessage);
    }

    private static PatientContextSnapshot Snapshot(string key) => new()
    { ContextKey = key, SnapshotRef = "ctx_v2_" + new string('1', 32), ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5) };

    private static IOptions<IReGoToolOptions> Settings(string path = "unused") => Options.Create(new IReGoToolOptions
    { SessionRefSigningKey = Key, ReportImage = new IReGoReportImageOptions { SigningKey = Key, StoragePath = path } });

    private static IReGoToolsController Controller() => new(null!, null!, null!, null!, null!, null!, null!,
        Settings(), NullLogger<IReGoToolsController>.Instance)
    { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };

    private static string Base64(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private sealed class NeverEvaluate : IIReGoPatientEvaluationService
    {
        public Task<IReGoPatientEvaluation> EvaluateAsync(TrustedPatientContext context, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Retired references must be rejected before accessing the database.");
    }
}
