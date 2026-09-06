using AI_WebApi.IServices.AITools;
using AI_WebApi.Model.AITools;
using AI_WebApi.Model.Domain.IReGo;
using AI_WebApi.Model.DTOs.AITools;
using AI_WebApi.Services.AITools.IReGo;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AI_WebApi.Tests;

public sealed class IReGoSingleSessionReportServiceTests
{
    [Fact]
    public async Task Completed_session_is_rendered_once_and_returned_by_stable_artifact_url()
    {
        var options = OptionsValue();
        var renderer = new FakeRenderer();
        var store = new EphemeralIReGoReportArtifactStore(Options.Create(options));
        var service = new IReGoSingleSessionReportService(
            new FakeAnalysisService("completed"),
            new FakeHtmlBuilder(),
            renderer,
            store,
            Options.Create(options),
            NullLogger<IReGoSingleSessionReportService>.Instance);
        var request = Request();

        var first = await service.GenerateAsync(
            Context(), request, "https://reports.example.test", "req-1");
        var second = await service.GenerateAsync(
            Context(), request, "https://reports.example.test", "req-2");

        Assert.Equal(ToolStatuses.Available, first.Status);
        Assert.NotNull(first.Data);
        Assert.Equal(first.Data!.ArtifactRef, second.Data!.ArtifactRef);
        Assert.StartsWith(
            "https://reports.example.test/api/ai/patients/report-artifacts/",
            first.Data.ImageUrl);
        Assert.EndsWith("/content", first.Data.ImageUrl);
        Assert.Null(first.Data.ExpiresAt);
        Assert.Contains(first.Data.ImageUrl, first.Data.DisplayMarkdown);
        Assert.Equal(1, renderer.CallCount);

        var image = service.ResolveArtifact(first.Data.ArtifactRef);
        Assert.Equal(ReportImageAccessStatus.Available, image.Status);
        Assert.Equal(FakeRenderer.Png, image.Bytes);
    }

    [Fact]
    public async Task Non_completed_session_is_not_rendered()
    {
        var options = OptionsValue();
        var renderer = new FakeRenderer();
        var service = new IReGoSingleSessionReportService(
            new FakeAnalysisService("execution_unknown"),
            new FakeHtmlBuilder(),
            renderer,
            new EphemeralIReGoReportArtifactStore(Options.Create(options)),
            Options.Create(options),
            NullLogger<IReGoSingleSessionReportService>.Instance);

        var response = await service.GenerateAsync(
            Context(), Request(), "https://reports.example.test", "req-1");

        Assert.Equal(ToolStatuses.Unavailable, response.Status);
        Assert.Contains(
            IReGoReasonCodes.SingleReportRequiresCompletedSession,
            response.ReasonCodes);
        Assert.Equal(0, renderer.CallCount);
    }

    [Fact]
    public async Task Missing_ai_evaluation_is_computed_by_backend()
    {
        var options = OptionsValue();
        var renderer = new FakeRenderer();
        var service = new IReGoSingleSessionReportService(
            new FakeAnalysisService("completed"),
            new FakeHtmlBuilder(),
            renderer,
            new EphemeralIReGoReportArtifactStore(Options.Create(options)),
            Options.Create(options),
            NullLogger<IReGoSingleSessionReportService>.Instance);
        var request = new GenerateIReGoSingleSessionReportRequestDto
        {
            SystemContext = new DifySystemContextDto { User = "7" },
            SessionRef = "irego_s_v1_0123456789abcdef0123456789abcdef"
        };

        var response = await service.GenerateAsync(
            Context(), request, "https://reports.example.test", "req-1");

        Assert.Equal(ToolStatuses.Available, response.Status);
        Assert.Equal(1, renderer.CallCount);
    }

    [Fact]
    public async Task Request_contract_has_no_caller_evaluation_input()
    {
        var options = OptionsValue();
        var renderer = new FakeRenderer();
        var service = new IReGoSingleSessionReportService(
            new FakeAnalysisService("completed"),
            new FakeHtmlBuilder(),
            renderer,
            new EphemeralIReGoReportArtifactStore(Options.Create(options)),
            Options.Create(options),
            NullLogger<IReGoSingleSessionReportService>.Instance);
        var request = new GenerateIReGoSingleSessionReportRequestDto
        {
            SystemContext = new DifySystemContextDto { User = "7" },
            SessionRef = "irego_s_v1_0123456789abcdef0123456789abcdef"
        };

        var response = await service.GenerateAsync(
            Context(), request, "https://reports.example.test", "req-1");

        Assert.Equal(ToolStatuses.Available, response.Status);
        Assert.Equal(1, renderer.CallCount);
    }

    [Fact]
    public async Task Repeated_generation_does_not_change_artifact()
    {
        var options = OptionsValue();
        var renderer = new FakeRenderer();
        var service = new IReGoSingleSessionReportService(
            new FakeAnalysisService("completed", includeGame: true),
            new FakeHtmlBuilder(),
            renderer,
            new EphemeralIReGoReportArtifactStore(Options.Create(options)),
            Options.Create(options),
            NullLogger<IReGoSingleSessionReportService>.Instance);
        var firstRequest = Request();
        var secondRequest = Request();

        var first = await service.GenerateAsync(
            Context(), firstRequest, "https://reports.example.test", "req-1");
        var second = await service.GenerateAsync(
            Context(), secondRequest, "https://reports.example.test", "req-2");

        Assert.Equal(ToolStatuses.Available, first.Status);
        Assert.Equal(ToolStatuses.Available, second.Status);
        Assert.Equal(first.Data!.ArtifactRef, second.Data!.ArtifactRef);
        Assert.Equal(1, renderer.CallCount);
    }

    [Fact]
    public async Task In_place_report_metric_change_creates_a_new_artifact()
    {
        var options = OptionsValue();
        var renderer = new FakeRenderer();
        var analysis = new FakeAnalysisService("completed");
        var service = new IReGoSingleSessionReportService(
            analysis,
            new FakeHtmlBuilder(),
            renderer,
            new EphemeralIReGoReportArtifactStore(Options.Create(options)),
            Options.Create(options),
            NullLogger<IReGoSingleSessionReportService>.Instance);

        var first = await service.GenerateAsync(
            Context(), Request(), "https://reports.example.test", "req-1");
        analysis.WalkingDistance = 125m;
        var second = await service.GenerateAsync(
            Context(), Request(), "https://reports.example.test", "req-2");

        Assert.NotEqual(first.Data!.ArtifactRef, second.Data!.ArtifactRef);
        Assert.Equal(2, renderer.CallCount);
    }

    [Fact]
    public async Task In_place_plan_assistance_change_creates_a_new_artifact()
    {
        var options = OptionsValue();
        var renderer = new FakeRenderer();
        var analysis = new FakeAnalysisService("completed");
        var service = new IReGoSingleSessionReportService(
            analysis,
            new FakeHtmlBuilder(),
            renderer,
            new EphemeralIReGoReportArtifactStore(Options.Create(options)),
            Options.Create(options),
            NullLogger<IReGoSingleSessionReportService>.Instance);

        var first = await service.GenerateAsync(
            Context(), Request(), "https://reports.example.test", "req-1");
        analysis.PlanAssistance = 35m;
        var second = await service.GenerateAsync(
            Context(), Request(), "https://reports.example.test", "req-2");

        Assert.NotEqual(first.Data!.ArtifactRef, second.Data!.ArtifactRef);
        Assert.Equal(2, renderer.CallCount);
    }

    [Fact]
    public async Task Watermark_and_non_report_quality_changes_do_not_change_artifact()
    {
        var options = OptionsValue();
        var renderer = new FakeRenderer();
        var analysis = new FakeAnalysisService("completed");
        var service = new IReGoSingleSessionReportService(
            analysis,
            new FakeHtmlBuilder(),
            renderer,
            new EphemeralIReGoReportArtifactStore(Options.Create(options)),
            Options.Create(options),
            NullLogger<IReGoSingleSessionReportService>.Instance);

        var first = await service.GenerateAsync(
            Context(), Request(), "https://reports.example.test", "req-1");
        analysis.Watermark = "unrelated-new-watermark";
        analysis.QualityMessage = "仅用于工具诊断，不进入当前报告";
        var second = await service.GenerateAsync(
            Context(), Request(), "https://reports.example.test", "req-2");

        Assert.Equal(first.Data!.ArtifactRef, second.Data!.ArtifactRef);
        Assert.Equal(1, renderer.CallCount);
    }

    [Fact]
    public async Task Stable_block_order_does_not_depend_on_source_collection_order()
    {
        var options = OptionsValue();
        var renderer = new FakeRenderer();
        var analysis = new FakeAnalysisService("completed", includeGame: true);
        var service = new IReGoSingleSessionReportService(
            analysis,
            new FakeHtmlBuilder(),
            renderer,
            new EphemeralIReGoReportArtifactStore(Options.Create(options)),
            Options.Create(options),
            NullLogger<IReGoSingleSessionReportService>.Instance);

        var first = await service.GenerateAsync(
            Context(), Request(), "https://reports.example.test", "req-1");
        analysis.ReverseReportBlocks = true;
        var second = await service.GenerateAsync(
            Context(), Request(), "https://reports.example.test", "req-2");

        Assert.Equal(first.Data!.ArtifactRef, second.Data!.ArtifactRef);
        Assert.Equal(1, renderer.CallCount);
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
            TemplateVersion = "irego-single-v2"
        }
    };

    private static TrustedPatientContext Context() => new()
    {
        PatientId = 7,
        SubjectRef = "dify_user:7"
    };

    private static GenerateIReGoSingleSessionReportRequestDto Request() => new()
    {
        SystemContext = new DifySystemContextDto { User = "7" },
        SessionRef = "irego_s_v1_0123456789abcdef0123456789abcdef"
    };

    private sealed class FakeAnalysisService : IIReGoSessionAnalysisService
    {
        private readonly string _state;

        private readonly bool _includeGame;

        public decimal WalkingDistance { get; set; } = 100m;
        public decimal PlanAssistance { get; set; } = 30m;
        public string Watermark { get; set; } = "watermark";
        public string? QualityMessage { get; set; }
        public bool ReverseReportBlocks { get; set; }

        public FakeAnalysisService(string state, bool includeGame = false)
        {
            _state = state;
            _includeGame = includeGame;
        }

        public Task<ToolResponseDto<IReGoSessionAnalysisDto>> GetAsync(
            TrustedPatientContext context,
            GetIReGoSessionAnalysisRequestDto request,
            string requestId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ToolResponseDto<IReGoSessionAnalysisDto>.Create(
                "get_irego_session_analysis",
                requestId,
                ToolStatuses.Success,
                new IReGoSessionAnalysisDto
                {
                    Session = new IReGoSessionIdentityDto
                    {
                        PatientRef = "irego_patient_v1_opaque",
                        SessionRef = request.SessionRef ?? string.Empty
                    },
                    Time = new IReGoSessionTimeDto
                    {
                        TrainingState = _state
                    },
                    Schema = new IReGoSessionSchemaDto
                    {
                        ReportSchemaSignature = "schema-signature"
                    },
                    Plan = new IReGoPlanDetailDto
                    {
                        Blocks = new[]
                        {
                            new IReGoContentBlockDto
                            {
                                Sequence = 1,
                                RehabContent = "walking",
                                DisplayName = "计划步行训练第1段",
                                Metrics = new[]
                                {
                                    new MetricFactDto
                                    {
                                        MetricCode = "plan_assistance",
                                        RawValue = PlanAssistance,
                                        NormalizedValue = PlanAssistance,
                                        Unit = "%",
                                        UnitStatus = "known",
                                        ValueStatus = "valid"
                                    }
                                }
                            }
                        }
                    },
                    ReportBlocks = BuildReportBlocks(),
                    BlockComparison = new[]
                    {
                        new IReGoBlockComparisonDto
                        {
                            PlanSequence = 1,
                            ReportSequence = 1,
                            Status = "matched_exact"
                        }
                    },
                    Quality = new QualitySummaryDto
                    {
                        PatientMessage = QualityMessage
                    }
                },
                Array.Empty<string>(),
                new ToolMetaDto
                {
                    SourceCompleteness = "authoritative_primary",
                    Watermark = Watermark
                }));

        private IReadOnlyList<IReGoContentBlockDto> BuildReportBlocks()
        {
            var blocks = new List<IReGoContentBlockDto>
            {
                new()
                {
                    Sequence = 1,
                    RehabContent = "walking",
                    DisplayName = "步行训练第1段",
                    Metrics = new[]
                    {
                        new MetricFactDto
                        {
                            MetricCode = "walking_distance",
                            RawValue = WalkingDistance,
                            NormalizedValue = WalkingDistance,
                            Unit = "m",
                            UnitStatus = "known",
                            ValueStatus = "valid"
                        }
                    }
                }
            };
            if (_includeGame)
            {
                blocks.Add(new IReGoContentBlockDto
                {
                    Sequence = 2,
                    RehabContent = "game",
                    InteractionType = "game",
                    DisplayName = "宇宙拼图",
                    Metrics = new[]
                    {
                        new MetricFactDto
                        {
                            MetricCode = "game_score",
                            RawValue = 10,
                            NormalizedValue = 10m,
                            ValueStatus = "valid"
                        }
                    }
                });
            }

            return ReverseReportBlocks
                ? blocks.AsEnumerable().Reverse().ToArray()
                : blocks;
        }
    }

    private sealed class FakeHtmlBuilder : IIReGoSingleSessionReportHtmlBuilder
    {
        public string Build(
            IReGoSessionAnalysisDto analysis,
            IReGoSingleSessionAiEvaluationDto aiEvaluation) =>
            "<html><body>report</body></html>";
    }

    private sealed class FakeRenderer : IIReGoReportImageRenderer
    {
        public static readonly byte[] Png = { 137, 80, 78, 71 };
        public int CallCount { get; private set; }

        public Task<byte[]> RenderPngAsync(
            string html,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(Png);
        }
    }
}
