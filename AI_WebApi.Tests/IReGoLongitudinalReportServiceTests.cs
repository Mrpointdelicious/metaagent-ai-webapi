using AI_WebApi.IServices.AITools;
using AI_WebApi.Model.AITools;
using AI_WebApi.Model.Domain.IReGo;
using AI_WebApi.Model.DTOs.AITools;
using AI_WebApi.Services.AITools.IReGo;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AI_WebApi.Tests;

public sealed class IReGoLongitudinalReportServiceTests
{
    [Fact]
    public async Task Report_is_rendered_once_and_returned_as_stable_artifact_url()
    {
        var options = OptionsValue();
        var renderer = new FakeRenderer();
        var service = new IReGoLongitudinalReportService(
            new FakeAnalysisService(completeCount: 2),
            new FakeHtmlBuilder(),
            renderer,
            new EphemeralIReGoReportArtifactStore(Options.Create(options)),
            Options.Create(options),
            NullLogger<IReGoLongitudinalReportService>.Instance);

        var first = await service.GenerateAsync(
            Context(), Request(), "https://reports.example.test", "request-1");
        var second = await service.GenerateAsync(
            Context(), Request(), "https://reports.example.test", "request-2");

        Assert.Equal(ToolStatuses.Available, first.Status);
        Assert.Equal(first.Data!.ArtifactRef, second.Data!.ArtifactRef);
        Assert.StartsWith(
            "https://reports.example.test/api/ai/patients/report-artifacts/",
            first.Data.ImageUrl);
        Assert.EndsWith("/content", first.Data.ImageUrl);
        Assert.Equal("ephemeral_memory", first.Data.StorageMode);
        Assert.Null(first.Data.ExpiresAt);
        Assert.Equal("irego-longitudinal-v1", first.Data.TemplateVersion);
        Assert.Equal(1, renderer.CallCount);
    }

    [Fact]
    public async Task Fewer_than_two_complete_reports_are_not_rendered()
    {
        var options = OptionsValue();
        var renderer = new FakeRenderer();
        var service = new IReGoLongitudinalReportService(
            new FakeAnalysisService(completeCount: 1),
            new FakeHtmlBuilder(),
            renderer,
            new EphemeralIReGoReportArtifactStore(Options.Create(options)),
            Options.Create(options),
            NullLogger<IReGoLongitudinalReportService>.Instance);

        var response = await service.GenerateAsync(
            Context(), Request(), "https://reports.example.test", "request-1");

        Assert.Equal(ToolStatuses.Unavailable, response.Status);
        Assert.Contains(
            IReGoReasonCodes.LongitudinalRequiresTwoReports,
            response.ReasonCodes);
        Assert.Equal(0, renderer.CallCount);
    }

    [Fact]
    public async Task Fewer_than_three_valid_project_records_are_not_rendered()
    {
        var options = OptionsValue();
        var renderer = new FakeRenderer();
        var service = new IReGoLongitudinalReportService(
            new FakeAnalysisService(completeCount: 2, validPointCount: 2),
            new FakeHtmlBuilder(),
            renderer,
            new EphemeralIReGoReportArtifactStore(Options.Create(options)),
            Options.Create(options),
            NullLogger<IReGoLongitudinalReportService>.Instance);

        var response = await service.GenerateAsync(
            Context(), Request(), "https://reports.example.test", "request-1");

        Assert.Equal(ToolStatuses.Unavailable, response.Status);
        Assert.Contains(
            IReGoReasonCodes.LongitudinalEvaluationInsufficientData,
            response.ReasonCodes);
        Assert.Equal(0, renderer.CallCount);
    }

    [Fact]
    public async Task In_place_metric_point_change_creates_a_new_artifact()
    {
        var options = OptionsValue();
        var renderer = new FakeRenderer();
        var analysis = new FakeAnalysisService(completeCount: 2);
        var service = new IReGoLongitudinalReportService(
            analysis,
            new FakeHtmlBuilder(),
            renderer,
            new EphemeralIReGoReportArtifactStore(Options.Create(options)),
            Options.Create(options),
            NullLogger<IReGoLongitudinalReportService>.Instance);

        var first = await service.GenerateAsync(
            Context(), Request(), "https://reports.example.test", "request-1");
        analysis.MiddlePointAdjustment = 10m;
        var second = await service.GenerateAsync(
            Context(), Request(), "https://reports.example.test", "request-2");

        Assert.NotEqual(first.Data!.ArtifactRef, second.Data!.ArtifactRef);
        Assert.Equal(2, renderer.CallCount);
    }

    [Fact]
    public async Task Watermark_and_equivalent_selection_input_do_not_change_artifact()
    {
        var options = OptionsValue();
        var renderer = new FakeRenderer();
        var analysis = new FakeAnalysisService(completeCount: 2);
        var service = new IReGoLongitudinalReportService(
            analysis,
            new FakeHtmlBuilder(),
            renderer,
            new EphemeralIReGoReportArtifactStore(Options.Create(options)),
            Options.Create(options),
            NullLogger<IReGoLongitudinalReportService>.Instance);

        var first = await service.GenerateAsync(
            Context(), Request("all"), "https://reports.example.test", "request-1");
        analysis.Watermark = "unrelated-new-watermark";
        var second = await service.GenerateAsync(
            Context(), Request("walking"), "https://reports.example.test", "request-2");

        Assert.Equal(first.Data!.ArtifactRef, second.Data!.ArtifactRef);
        Assert.Equal(1, renderer.CallCount);
    }

    [Fact]
    public async Task Resolved_training_scope_change_creates_a_new_artifact()
    {
        var options = OptionsValue();
        var renderer = new FakeRenderer();
        var analysis = new FakeAnalysisService(completeCount: 2);
        var service = new IReGoLongitudinalReportService(
            analysis,
            new FakeHtmlBuilder(),
            renderer,
            new EphemeralIReGoReportArtifactStore(Options.Create(options)),
            Options.Create(options),
            NullLogger<IReGoLongitudinalReportService>.Instance);

        var first = await service.GenerateAsync(
            Context(), Request(), "https://reports.example.test", "request-1");
        analysis.SecondSessionRef = "irego_s_v1_replaced";
        var second = await service.GenerateAsync(
            Context(), Request(), "https://reports.example.test", "request-2");

        Assert.NotEqual(first.Data!.ArtifactRef, second.Data!.ArtifactRef);
        Assert.Equal(2, renderer.CallCount);
    }

    private static IReGoToolOptions OptionsValue() => new()
    {
        ReportImage = new IReGoReportImageOptions
        {
            SigningKey = new string('r', 40),
            UrlTtlSeconds = 300,
            CacheTtlSeconds = 600,
            MaxImageBytes = 1024 * 1024,
            MaxCacheBytes = 2 * 1024 * 1024,
            LongitudinalTemplateVersion = "irego-longitudinal-v1"
        }
    };

    private static TrustedPatientContext Context() => new()
    {
        PatientId = 7,
        SubjectRef = "dify_user:7"
    };

    private static GenerateIReGoLongitudinalReportRequestDto Request(
        string projectScope = "all") => new()
    {
        SystemContext = new DifySystemContextDto { User = "7" },
        Selector = "latest_contiguous",
        ReportCount = 2,
        ProjectScope = projectScope
    };

    private sealed class FakeAnalysisService : IIReGoLongitudinalAnalysisService
    {
        private readonly int _completeCount;
        private readonly int _validPointCount;

        public decimal MiddlePointAdjustment { get; set; }
        public string Watermark { get; set; } = "test-watermark";
        public string SecondSessionRef { get; set; } = "irego_s_v1_second";

        public FakeAnalysisService(int completeCount, int validPointCount = 3)
        {
            _completeCount = completeCount;
            _validPointCount = validPointCount;
        }

        public Task<ToolResponseDto<IReGoLongitudinalAnalysisDto>> GetAsync(
            TrustedPatientContext context,
            GetIReGoLongitudinalAnalysisRequestDto request,
            string requestId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ToolResponseDto<IReGoLongitudinalAnalysisDto>.Create(
                "get_irego_longitudinal_analysis",
                requestId,
                ToolStatuses.Success,
                new IReGoLongitudinalAnalysisDto
                {
                    PatientRef = "irego_patient_v1_test",
                    Window = new IReGoLongitudinalWindowDto
                    {
                        ResolvedReportCount = 2,
                        CompleteReportCount = _completeCount,
                        IncompleteReportCount = 2 - _completeCount
                    },
                    Reports = new[]
                    {
                        new IReGoLongitudinalReportPositionDto
                        {
                            Ordinal = 1,
                            SessionRef = "irego_s_v1_first"
                        },
                        new IReGoLongitudinalReportPositionDto
                        {
                            Ordinal = 2,
                            SessionRef = SecondSessionRef
                        }
                    },
                    Projects = new[]
                    {
                        new IReGoLongitudinalProjectDto
                        {
                            Lanes = new[]
                            {
                                new IReGoLongitudinalLaneDto
                                {
                                    MetricSeries = new[]
                                    {
                                        new IReGoLongitudinalMetricSeriesDto
                                        {
                                            LongestSameConditionRun = 1,
                                            Points = Enumerable.Range(
                                                    1,
                                                    _validPointCount)
                                                .Select(index =>
                                                    new IReGoLongitudinalMetricPointDto
                                                    {
                                                        ReportOrdinal = index,
                                                        Status = "valid",
                                                        NormalizedValue = index == 2
                                                            ? index + MiddlePointAdjustment
                                                            : index
                                                    })
                                                .ToArray()
                                        }
                                    }
                                }
                            }
                        }
                    }
                },
                Array.Empty<string>(),
                new ToolMetaDto
                {
                    SourceCompleteness = "authoritative_primary",
                    Watermark = Watermark
                }));
    }

    private sealed class FakeHtmlBuilder : IIReGoLongitudinalReportHtmlBuilder
    {
        public string Build(IReGoLongitudinalAnalysisDto analysis) =>
            "<html><body>longitudinal</body></html>";
    }

    private sealed class FakeRenderer : IIReGoReportImageRenderer
    {
        public int CallCount { get; private set; }

        public Task<byte[]> RenderPngAsync(
            string html,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(new byte[] { 137, 80, 78, 71 });
        }
    }
}
