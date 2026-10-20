using AI_WebApi.IServices.AITools;
using AI_WebApi.Model.AITools;
using AI_WebApi.Model.Domain.IReGo;
using AI_WebApi.Model.Domain.IReTour;
using AI_WebApi.Model.DTOs.AITools;
using AI_WebApi.Model.Entities;
using AI_WebApi.Repository.AITools;
using AI_WebApi.Services.AITools.IReCog;
using AI_WebApi.Services.AITools.IReGo;
using AI_WebApi.Services.AITools.IReTour;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace AI_WebApi.Tests;

public sealed class TrainingToolsReleaseTests
{
    private static readonly TrustedPatientContext Context = new() { PatientId = 100, SubjectRef = "fixture" };
    private static readonly IOptions<IReTourToolOptions> TourOptions = Options.Create(new IReTourToolOptions { SessionRefSigningKey = new string('t', 40) });

    [Fact]
    public async Task Tour_window_preserves_missing_records_and_separates_field_aliases()
    {
        var source = new TourFixture();
        var service = new IReTourLongitudinalAnalysisService(source, new IReTourSessionRefProvider(TourOptions), TourOptions);
        var response = await service.GetAsync(Context, new() { ReportCount = 4 }, "fixture", default);
        Assert.Equal("partial", response.Status);
        Assert.Equal(4, response.Data!.Sessions.Count);
        Assert.Equal(1, response.Data.Window.ExcludedSessionCount);
        Assert.Equal(2, response.Data.MetricSeries.Count);
        var series = response.Data.MetricSeries.Single(s => s.SourceField == "avg_Speed");
        Assert.Null(series.Points[1].Value);
        Assert.Equal(1m, series.AbsoluteChange);
        Assert.Equal(64, response.Data.SourceSnapshot.Length);
        source.UniformFields = true;
        var preview = await service.GetAsync(Context, new() { ReportCount = 4 }, "fixture", default);
        SavePreview("iretour-multiple", new IReTourLongitudinalReportHtmlBuilder().Build(preview.Data!));
    }

    [Fact]
    public async Task Tour_single_and_reference_recheck_use_current_source()
    {
        var source = new TourFixture();
        var refs = new IReTourSessionRefProvider(TourOptions);
        var service = new IReTourSessionAnalysisService(source, refs, TourOptions);
        var reference = refs.Issue(source.Sessions[0]);
        Assert.Null(refs.Verify(reference, 101, source.Sessions));
        Assert.Null(refs.Verify(reference, 100, new[] { TourFixture.Session(1, 99) }));
        var first = await service.GetAsync(Context, new() { Selector = "session_ref", SessionRef = reference }, "fixture", default);
        Assert.Single(first.Data!.Result.Metrics);
        source.Value = "2.0";
        var changed = await service.GetAsync(Context, new() { Selector = "session_ref", SessionRef = reference }, "fixture", default);
        Assert.NotEqual(first.Data.SourceSnapshot, changed.Data!.SourceSnapshot);
        Assert.DoesNotContain("PRIVATE", JsonConvert.SerializeObject(first.Data));
        SavePreview("iretour-single", new IReTourSingleSessionReportHtmlBuilder().Build(first.Data));
    }

    [Fact]
    public async Task Cog_reports_keep_totals_separate_and_reject_stale_snapshot()
    {
        var fixture = new CogFixture();
        var renderer = new Renderer();
        var options = Options.Create(new IReGoToolOptions { ReportImage = new() { SigningKey = new string('r', 40) } });
        var service = new IReCogToolService(fixture, Options.Create(new IReCogToolOptions { SessionRefSigningKey = new string('c', 40) }),
            renderer, new EphemeralIReGoReportArtifactStore(options), options);
        var response = await service.ReadAsync("longitudinal_analysis", Context, new() { ReportCount = 2 }, "fixture", default);
        Assert.Equal(2, response.Data!.Sessions.Count);
        Assert.Equal("blocked_unverified_instrument_version", response.Data.Comparability);
        Assert.All(response.Data.Sessions, r => Assert.Equal(12m, r.ReportedScore));
        Assert.DoesNotContain("PRIVATE_ANSWER", JsonConvert.SerializeObject(response.Data));
        var reference = response.Data.Sessions[0].SessionRef;
        var denied = await service.ReadAsync("session_analysis", new() { PatientId = 999 }, new() { Selector = "session_ref", SessionRef = reference }, "fixture", default);
        Assert.Null(denied.Data);
        var report = await service.ReportAsync(true, Context, new() { ReportCount = 2, ExpectedSourceSnapshot = response.Data.SourceSnapshot }, "https://example.test", "fixture", default);
        Assert.NotNull(report.Data);
        Assert.Equal(1, renderer.Calls);
        fixture.Score = "13";
        var stale = await service.ReportAsync(true, Context, new() { ReportCount = 2, ExpectedSourceSnapshot = response.Data.SourceSnapshot }, "https://example.test", "fixture", default);
        Assert.Contains("IRECOG_SOURCE_CHANGED", stale.ReasonCodes);
        Assert.Equal(1, renderer.Calls);
        SavePreview("irecog", renderer.Html);
    }

    private static void SavePreview(string name, string html)
    {
        var preview = Environment.GetEnvironmentVariable("TRAINING_PREVIEW_DIR");
        if (!string.IsNullOrEmpty(preview))
        {
            Directory.CreateDirectory(preview);
            File.WriteAllText(Path.Combine(preview, name + ".html"), html);
        }
    }

    private sealed class TourFixture : IIReTourPatientEvaluationService
    {
        public string Value = "1.0";
        public bool UniformFields;
        public IReTourEvaluatedSession[] Sessions = Enumerable.Range(1, 4).Select(i => Session(i)).ToArray();
        public static IReTourEvaluatedSession Session(int i, int institution = 1) => new()
        {
            WalkId = i, CanonicalSessionKey = $"walk:{i}", PatientId = 100, BusinessId = institution,
            PatientBindingVersion = 1, PatientBindingNamespace = "dbuser", ItemId = 0,
            CanonicalCode = "straight_primary", SchemaFamily = "straight_summary_v1", DisplayName = "直行训练", DeviceVerified = true,
            ExecutionState = "completed", ResultState = i == 2 ? "no_result" : "summary_available",
            Time = new() { ExecutionStartedAt = DateTimeOffset.Parse($"2026-01-0{i}T10:00:00+08:00"), ExecutionEndedAt = DateTimeOffset.Parse($"2026-01-0{i}T10:02:00+08:00") }
        };
        public Task<IReTourEvaluationResult> EvaluateAsync(TrustedPatientContext context, string requestId, CancellationToken cancellationToken) => Task.FromResult(new IReTourEvaluationResult
        {
            PatientExists = true, BindingVerified = true, Sessions = Sessions,
            SummaryReportsByWalk = new Dictionary<int, IReadOnlyList<IReTourSummaryReportEntity>>
            {
                [1] = new[] { new IReTourSummaryReportEntity { Id = 1, PlanId = 1, ItemId = 0, Details = "{\"avg_Speed\":" + Value + ",\"phone\":\"PRIVATE\"}" } },
                [3] = new[] { new IReTourSummaryReportEntity { Id = 3, PlanId = 3, ItemId = 0, Details = "{\"avg_Speed\":2}" } },
                [4] = new[] { new IReTourSummaryReportEntity { Id = 4, PlanId = 4, ItemId = 0, Details = UniformFields ? "{\"avg_Speed\":3}" : "{\"g_averageWalkingSpeed\":3}" } }
            }
        });
    }
    private sealed class CogFixture : IIReCogDataRepository
    {
        public string Score = "12";
        public Task<IReCogSource> LoadAsync(TrustedPatientContext context, CancellationToken cancellationToken) => Task.FromResult(new IReCogSource
        {
            Patient = new() { Id = 100, BusinessId = 1, Status = 1, IsDeleted = 0 },
            Plans = Enumerable.Range(1, 2).Select(i => new RehabPlanEntity { Id = i, UserId = 100, BusinessId = 1, TemplateId = i, BookingTime = new DateTime(2026, 1, i) }).ToArray(),
            Reports = Enumerable.Range(1, 2).Select(i => new RehabReportEntity { Id = i, PlanId = i, ReportDetails = "{\"TableTitle\":\"合成量表\",\"Score\":" + Score + ",\"Title\":[\"测试题目\"],\"Options\":[[\"PRIVATE_ANSWER\"]]}" }).ToArray()
        });
    }
    private sealed class Renderer : IIReGoReportImageRenderer
    {
        public int Calls;
        public string Html = "";
        public Task<byte[]> RenderPngAsync(string html, CancellationToken cancellationToken = default)
        { Calls++; Html = html; return Task.FromResult(new byte[] { 1, 2, 3 }); }
    }
}
