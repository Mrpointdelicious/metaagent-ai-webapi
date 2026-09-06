using System.Text;
using AI_WebApi.Model.AITools;
using AI_WebApi.Model.DTOs.AITools;
using AI_WebApi.Services.AITools.HospitalOperations;
using Microsoft.Extensions.Options;

namespace AI_WebApi.Tests;

public sealed class HospitalOperationsArtifactStoreTests
{
    [Fact]
    public void Artifact_reference_is_stable_and_changes_with_report_content()
    {
        var options = Options.Create(Config("unused"));
        var provider = new HospitalOperationsArtifactIdentityProvider(options);

        var first = provider.Create(Result(62));
        var repeated = provider.Create(Result(62));
        var changed = provider.Create(Result(63));

        Assert.StartsWith("hospital_report_v2_", first.ArtifactRef);
        Assert.Equal(51, first.ArtifactRef.Length);
        Assert.Equal(first.ArtifactRef, repeated.ArtifactRef);
        Assert.Equal(first.QueryFingerprint, repeated.QueryFingerprint);
        Assert.Equal(first.ContentFingerprint, repeated.ContentFingerprint);
        Assert.NotEqual(first.ArtifactRef, changed.ArtifactRef);
        Assert.NotEqual(first.ContentFingerprint, changed.ContentFingerprint);
    }

    [Fact]
    public void Durable_store_survives_recreation_and_keeps_both_formats()
    {
        var path = TemporaryDirectory();
        try
        {
            var options = Options.Create(Config(path));
            var identity = new HospitalOperationsArtifactIdentityProvider(options)
                .Create(Result(62));
            var manifest = Manifest(identity);
            var firstStore = new FileSystemHospitalOperationsArtifactStore(options);

            var written = firstStore.Store(
                manifest,
                "<html>测试</html>",
                new byte[] { 137, 80, 78, 71 });
            var recreated = new FileSystemHospitalOperationsArtifactStore(options);
            var html = recreated.Resolve(identity.ArtifactRef, "html");
            var png = recreated.Resolve(identity.ArtifactRef, "png");

            Assert.False(written.Reused);
            Assert.Equal(HospitalReportAccessStatus.Available, html.Status);
            Assert.Equal("<html>测试</html>", Encoding.UTF8.GetString(html.Bytes));
            Assert.Equal("text/html; charset=utf-8", html.MimeType);
            Assert.Equal(HospitalReportAccessStatus.Available, png.Status);
            Assert.Equal("image/png", png.MimeType);
            Assert.True(recreated.TryGet(identity.ArtifactRef, out var loaded));
            Assert.Equal(identity.ContentFingerprint, loaded.ContentFingerprint);
        }
        finally
        {
            Cleanup(path);
        }
    }

    [Fact]
    public void Duplicate_store_reuses_original_files_without_overwrite()
    {
        var path = TemporaryDirectory();
        try
        {
            var options = Options.Create(Config(path));
            var identity = new HospitalOperationsArtifactIdentityProvider(options)
                .Create(Result(62));
            var store = new FileSystemHospitalOperationsArtifactStore(options);
            var manifest = Manifest(identity);

            store.Store(manifest, "<html>first</html>", new byte[] { 1, 2, 3 });
            var repeated = store.Store(
                manifest,
                "<html>second</html>",
                new byte[] { 9, 9, 9 });
            var html = store.Resolve(identity.ArtifactRef, "html");
            var png = store.Resolve(identity.ArtifactRef, "png");

            Assert.True(repeated.Reused);
            Assert.Equal("<html>first</html>", Encoding.UTF8.GetString(html.Bytes));
            Assert.Equal(new byte[] { 1, 2, 3 }, png.Bytes);
        }
        finally
        {
            Cleanup(path);
        }
    }

    [Fact]
    public void Rejects_unknown_reference_format_and_path_traversal()
    {
        var path = TemporaryDirectory();
        try
        {
            var store = new FileSystemHospitalOperationsArtifactStore(
                Options.Create(Config(path)));

            Assert.Equal(
                HospitalReportAccessStatus.Invalid,
                store.Resolve("../report", "png").Status);
            Assert.Equal(
                HospitalReportAccessStatus.Invalid,
                store.Resolve(
                    "hospital_report_v2_00000000000000000000000000000000",
                    "pdf").Status);
        }
        finally
        {
            Cleanup(path);
        }
    }

    [Fact]
    public async Task Concurrent_writes_publish_one_complete_artifact()
    {
        var path = TemporaryDirectory();
        try
        {
            var options = Options.Create(Config(path));
            var identity = new HospitalOperationsArtifactIdentityProvider(options)
                .Create(Result(62));
            var store = new FileSystemHospitalOperationsArtifactStore(options);
            var manifest = Manifest(identity);

            var writes = await Task.WhenAll(Enumerable.Range(0, 8)
                .Select(index => Task.Run(() => store.Store(
                    manifest,
                    $"<html>{index}</html>",
                    new byte[] { 137, 80, 78, (byte)index }))));

            Assert.Single(writes.Where(x => !x.Reused));
            Assert.Equal(7, writes.Count(x => x.Reused));
            Assert.True(store.TryGet(identity.ArtifactRef, out _));
            Assert.Equal(
                HospitalReportAccessStatus.Available,
                store.Resolve(identity.ArtifactRef, "html").Status);
            Assert.Equal(
                HospitalReportAccessStatus.Available,
                store.Resolve(identity.ArtifactRef, "png").Status);
        }
        finally
        {
            Cleanup(path);
        }
    }

    [Fact]
    public void Missing_persistent_signing_key_rejects_identity_generation()
    {
        var provider = new HospitalOperationsArtifactIdentityProvider(
            Options.Create(new HospitalOperationsOptions
            {
                ArtifactRefSigningKey = "too-short"
            }));

        Assert.False(provider.IsConfigured);
        Assert.Throws<InvalidOperationException>(() => provider.Create(Result(62)));
    }

    private static HospitalOperationsArtifactManifestDto Manifest(
        HospitalOperationsArtifactIdentityDto identity) => new()
        {
            ArtifactRef = identity.ArtifactRef,
            QueryFingerprint = identity.QueryFingerprint,
            ContentFingerprint = identity.ContentFingerprint,
            ContractVersion = identity.ContractVersion,
            AnalysisRuleVersion = identity.AnalysisRuleVersion,
            TemplateVersion = identity.TemplateVersion,
            RenderProfile = identity.RenderProfile,
            CreatedAt = DateTimeOffset.UtcNow
        };

    private static HospitalOperationsResultDto Result(int patientCount) => new()
    {
        Scope = "institution",
        Section = "all",
        AnalysisDepth = "standard",
        Period = new HospitalOperationsPeriodDto
        {
            StartDate = "2026-08-01",
            EndDate = "2026-08-31"
        },
        Hospitals = new[]
        {
            new HospitalOperationsSnapshotDto
            {
                Hospital = new HospitalDirectoryEntryDto
                {
                    HospitalId = 13,
                    HospitalName = "上海中医药大学附属岳阳医院",
                    TenantLevel = 3
                },
                Overview = new HospitalOverviewDto
                {
                    PatientCount = patientCount,
                    DoctorCount = 46,
                    DailyActiveCount = 1
                },
                DerivedMetrics = new[]
                {
                    new HospitalOperationsMetricDto
                    {
                        Code = "daily_active_rate",
                        Label = "患者活跃率",
                        Value = 1.61m,
                        Unit = "%",
                        DisplayValue = "1.61%"
                    }
                }
            }
        },
        Analysis = new[]
        {
            new HospitalOperationsAnalysisItemDto
            {
                Code = "service_overview",
                Title = "服务概况",
                Text = $"注册患者{patientCount}人。",
                HospitalId = 13
            }
        }
    };

    private static HospitalOperationsOptions Config(string path) => new()
    {
        ArtifactRefSigningKey = new string('h', 40),
        ArtifactStoragePath = path,
        MaxArtifactBytes = 1024 * 1024,
        AnalysisRuleVersion = "hospital-analysis-rules-v1",
        TemplateVersion = "hospital-operations-v2",
        RenderProfile = "html-png-v1"
    };

    private static string TemporaryDirectory() => Path.Combine(
        Path.GetTempPath(),
        "ai-webapi-hospital-artifact-tests",
        Guid.NewGuid().ToString("N"));

    private static void Cleanup(string path)
    {
        var root = Path.GetFullPath(Path.Combine(
            Path.GetTempPath(),
            "ai-webapi-hospital-artifact-tests"));
        var target = Path.GetFullPath(path);
        if (target.StartsWith(root + Path.DirectorySeparatorChar) &&
            Directory.Exists(target))
        {
            Directory.Delete(target, true);
        }
    }
}
