using AI_WebApi.Model.AITools;
using AI_WebApi.Model.Domain.IReGo;
using AI_WebApi.Model.DTOs.AITools;
using AI_WebApi.Services.AITools.IReGo;

namespace AI_WebApi.Tests;

public sealed class PatientContextSnapshotCacheTests
{
    [Fact]
    public void Adding_latest_session_binding_does_not_replace_patient_snapshot()
    {
        var cache = new InMemoryPatientContextSnapshotCache();
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(5);
        cache.Store(new PatientContextSnapshot
        {
            SnapshotRef = "ctx_v2_0123456789abcdef0123456789abcdef",
            ContextKey = IReGoDataVersion.CacheNamespace + "|tenant|patient|role",
            ExpiresAt = expiresAt,
            Data = new MultisourcePatientContextDto
            {
                PatientRef = "patient:7"
            }
        });

        cache.StoreSession(new IReGoSessionBinding
        {
            SessionRef = "irego_s_v2_0123456789abcdef0123456789abcdef",
            ContextKey = IReGoDataVersion.CacheNamespace + "|tenant|patient|role",
            PlanId = 1,
            ReportId = 2,
            ExpiresAt = expiresAt
        });

        Assert.True(
            cache.TryGetSnapshot(IReGoDataVersion.CacheNamespace + "|tenant|patient|role", out var snapshot));
        Assert.Equal("patient:7", snapshot.Data.PatientRef);
        Assert.True(
            cache.TryResolveSession(
                IReGoDataVersion.CacheNamespace + "|tenant|patient|role",
                "irego_s_v2_0123456789abcdef0123456789abcdef",
                out _));
    }

    [Fact]
    public void Session_reference_is_bound_to_context_key()
    {
        var cache = new InMemoryPatientContextSnapshotCache();
        cache.StoreSession(new IReGoSessionBinding
        {
            SessionRef = "irego_s_v2_0123456789abcdef0123456789abcdef",
            ContextKey = IReGoDataVersion.CacheNamespace + "|tenant-a|patient:7|patient",
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5)
        });

        Assert.False(
            cache.TryResolveSession(
                IReGoDataVersion.CacheNamespace + "|tenant-b|patient:7|patient",
                "irego_s_v2_0123456789abcdef0123456789abcdef",
                out _));
    }
}
