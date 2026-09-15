using AI_WebApi.Model.AITools;
using AI_WebApi.Model.DTOs.AITools;
using AI_WebApi.Services.AITools.IReGo;
using Microsoft.Extensions.Options;

namespace AI_WebApi.Tests;

public sealed class IReGoReportArtifactStoreTests
{
    [Fact]
    public void Artifact_reference_is_stable_opaque_and_identity_sensitive()
    {
        var store = Store();

        var first = store.CreateArtifactRef(
            "irego_patient_v2_subject",
            "irego_s_v2_session",
            "snapshot-a",
            "irego-single-v1");
        var second = store.CreateArtifactRef(
            "irego_patient_v2_subject",
            "irego_s_v2_session",
            "snapshot-a",
            "irego-single-v1");
        var changedSource = store.CreateArtifactRef(
            "irego_patient_v2_subject",
            "irego_s_v2_session",
            "snapshot-b",
            "irego-single-v1");
        var changedPatient = store.CreateArtifactRef(
            "irego_patient_v2_another",
            "irego_s_v2_session",
            "snapshot-a",
            "irego-single-v1");
        var changedTemplate = store.CreateArtifactRef(
            "irego_patient_v2_subject",
            "irego_s_v2_session",
            "snapshot-a",
            "irego-single-v2");

        Assert.Equal(first, second);
        Assert.NotEqual(first, changedSource);
        Assert.NotEqual(first, changedPatient);
        Assert.NotEqual(first, changedTemplate);
        Assert.StartsWith("irego_img_v3_", first);
        Assert.Equal("irego_img_v3_".Length + 64, first.Length);
        Assert.DoesNotContain("subject", first);
        Assert.DoesNotContain("session", first);
    }

    [Fact]
    public void Signed_token_returns_memory_image_and_rejects_tampering()
    {
        var store = Store();
        const string artifactRef =
            "irego_img_v3_0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
        var bytes = new byte[] { 137, 80, 78, 71 };
        store.Store(artifactRef, bytes);
        var token = store.IssueAccessToken(
            artifactRef,
            DateTimeOffset.UtcNow.AddMinutes(2));

        var available = store.Resolve(token);
        var tokenParts = token.Split('.');
        tokenParts[2] = (tokenParts[2][0] == 'a' ? 'b' : 'a') + tokenParts[2][1..];
        var tampered = store.Resolve(string.Join('.', tokenParts));

        Assert.Equal(ReportImageAccessStatus.Available, available.Status);
        Assert.Equal(bytes, available.Bytes);
        Assert.Equal(ReportImageAccessStatus.Invalid, tampered.Status);
    }

    [Fact]
    public void Expired_token_fails_closed()
    {
        var store = Store();
        const string artifactRef =
            "irego_img_v3_0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
        store.Store(artifactRef, new byte[] { 1 });
        var token = store.IssueAccessToken(
            artifactRef,
            DateTimeOffset.UtcNow.AddSeconds(-1));

        Assert.Equal(ReportImageAccessStatus.Expired, store.Resolve(token).Status);
    }

    [Fact]
    public void Durable_store_survives_process_store_recreation()
    {
        var storagePath = Path.Combine(
            Path.GetTempPath(),
            "ai-webapi-report-artifact-tests",
            Guid.NewGuid().ToString("N"));
        try
        {
            var options = DurableOptions(storagePath);
            var firstStore = new FileSystemIReGoReportArtifactStore(
                Options.Create(options));
            var artifactRef = firstStore.CreateArtifactRef(
                "irego_patient_v2_subject",
                "irego_s_v2_session",
                "snapshot-a",
                "irego-single-v2");
            var bytes = new byte[] { 137, 80, 78, 71, 13, 10 };
            firstStore.Store(artifactRef, bytes);
            firstStore.Store(artifactRef, new byte[] { 9, 9, 9 });

            var recreatedStore = new FileSystemIReGoReportArtifactStore(
                Options.Create(options));
            var resolved = recreatedStore.ResolveArtifact(artifactRef);

            Assert.Equal("durable_filesystem", recreatedStore.StorageMode);
            Assert.True(File.Exists(
                Path.Combine(storagePath, IReGoDataVersion.StorageDirectory, artifactRef + ".png")));
            Assert.Equal(ReportImageAccessStatus.Available, resolved.Status);
            Assert.Equal(artifactRef, resolved.ArtifactRef);
            Assert.Equal(bytes, resolved.Bytes);
        }
        finally
        {
            if (Directory.Exists(storagePath))
            {
                Directory.Delete(storagePath, recursive: true);
            }
        }
    }

    [Fact]
    public void Durable_store_rejects_legacy_artifacts_even_when_the_file_exists()
    {
        var storagePath = Path.Combine(
            Path.GetTempPath(),
            "ai-webapi-report-artifact-tests",
            Guid.NewGuid().ToString("N"));
        try
        {
            var store = new FileSystemIReGoReportArtifactStore(
                Options.Create(DurableOptions(storagePath)));
            const string legacyArtifactRef =
                "irego_img_v1_0123456789abcdef0123456789abcdef";
            var bytes = new byte[] { 137, 80, 78, 71 };

            File.WriteAllBytes(Path.Combine(storagePath, legacyArtifactRef + ".png"), bytes);
            File.WriteAllBytes(Path.Combine(storagePath, IReGoDataVersion.StorageDirectory, legacyArtifactRef + ".png"), bytes);
            var resolved = store.ResolveArtifact(legacyArtifactRef);
            Assert.Equal(ReportImageAccessStatus.Invalid, resolved.Status);
            Assert.False(store.TryGet(legacyArtifactRef, out _));
            Assert.Throws<InvalidOperationException>(() => store.Store(legacyArtifactRef, bytes));
            Assert.Throws<InvalidOperationException>(() => store.IssueAccessToken(legacyArtifactRef, DateTimeOffset.UtcNow.AddMinutes(1)));
        }
        finally
        {
            if (Directory.Exists(storagePath))
            {
                Directory.Delete(storagePath, recursive: true);
            }
        }
    }

    [Fact]
    public void Durable_store_rejects_path_traversal_as_an_artifact_reference()
    {
        var storagePath = Path.Combine(
            Path.GetTempPath(),
            "ai-webapi-report-artifact-tests",
            Guid.NewGuid().ToString("N"));
        try
        {
            var store = new FileSystemIReGoReportArtifactStore(
                Options.Create(DurableOptions(storagePath)));

            var resolved = store.ResolveArtifact("../patient-report.png");

            Assert.Equal(ReportImageAccessStatus.Invalid, resolved.Status);
            Assert.Throws<InvalidOperationException>(() =>
                store.Store("../patient-report.png", new byte[] { 1 }));
        }
        finally
        {
            if (Directory.Exists(storagePath))
            {
                Directory.Delete(storagePath, recursive: true);
            }
        }
    }

    [Fact]
    public void Expired_current_token_keeps_current_artifact_reference_for_refresh()
    {
        var storagePath = Path.Combine(
            Path.GetTempPath(),
            "ai-webapi-report-artifact-tests",
            Guid.NewGuid().ToString("N"));
        try
        {
            var store = new FileSystemIReGoReportArtifactStore(
                Options.Create(DurableOptions(storagePath)));
            var artifactRef = store.CreateArtifactRef(
                "irego_patient_v2_subject",
                "irego_s_v2_session",
                "snapshot-a",
                "irego-single-v2");
            store.Store(artifactRef, new byte[] { 1, 2, 3 });
            var token = store.IssueAccessToken(
                artifactRef,
                DateTimeOffset.UtcNow.AddSeconds(-1));

            var expired = store.Resolve(token);

            Assert.Equal(ReportImageAccessStatus.Expired, expired.Status);
            Assert.Equal(artifactRef, expired.ArtifactRef);
            Assert.Equal(
                ReportImageAccessStatus.Available,
                store.ResolveArtifact(expired.ArtifactRef).Status);
        }
        finally
        {
            if (Directory.Exists(storagePath))
            {
                Directory.Delete(storagePath, recursive: true);
            }
        }
    }

    private static EphemeralIReGoReportArtifactStore Store() =>
        new(Options.Create(new IReGoToolOptions
        {
            ReportImage = new IReGoReportImageOptions
            {
                SigningKey = new string('r', 40),
                CacheTtlSeconds = 600,
                MaxImageBytes = 1024 * 1024,
                MaxCacheBytes = 2 * 1024 * 1024
            }
        }));

    private static IReGoToolOptions DurableOptions(string storagePath) => new()
    {
        ReportImage = new IReGoReportImageOptions
        {
            SigningKey = new string('r', 40),
            StoragePath = storagePath,
            MaxImageBytes = 1024 * 1024
        }
    };
}
