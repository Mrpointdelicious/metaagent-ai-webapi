using AI_WebApi.IServices.AITools;
using AI_WebApi.Model.AITools;
using AI_WebApi.Model.Domain.IReGo;
using AI_WebApi.Model.DTOs.AITools;
using AI_WebApi.Services.AITools.IReGo;
using AI_WebApi.Services.AITools.IReTour;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AI_WebApi.Tests;

public sealed class IReTourReportServiceTests
{
    [Theory]
    [InlineData("activity")]
    [InlineData("parameter")]
    [InlineData("metric")]
    public async Task Single_report_refreshes_when_display_content_changes(string field)
    {
        var analysis = new FakeSessionAnalysisService();
        var renderer = new FakeRenderer();
        var service = SingleService(analysis, renderer);
        var request = new GenerateIReTourSingleSessionReportRequestDto
        {
            SessionRef = "iretour_s_v1_test"
        };
        var first = await service.GenerateAsync(
            Context(), request, "https://reports.example.test", "first", CancellationToken.None);

        if (field == "activity") analysis.ActivityName = "更新后的项目名称";
        if (field == "parameter") analysis.ParameterName = "更新后的参数名称";
        if (field == "metric") analysis.MetricName = "更新后的指标名称";
        var second = await service.GenerateAsync(
            Context(), request, "https://reports.example.test", "second", CancellationToken.None);

        Assert.NotEqual(first.Data!.ArtifactRef, second.Data!.ArtifactRef);
        Assert.Equal(2, renderer.CallCount);
    }

    [Theory]
    [InlineData(false, "summary_available", "valid", IReTourReasonCodes.SingleReportQualityBlocked)]
    [InlineData(true, "raw_only", "valid", IReTourReasonCodes.SingleReportHasNoMetrics)]
    [InlineData(true, "summary_available", "invalid", IReTourReasonCodes.SingleReportHasNoMetrics)]
    public async Task Single_report_does_not_render_unusable_results(
        bool usable, string resultState, string valueStatus, string reason)
    {
        var renderer = new FakeRenderer();
        var service = SingleService(new FakeSessionAnalysisService
        {
            Usable = usable,
            ResultState = resultState,
            ValueStatus = valueStatus
        }, renderer);

        var response = await service.GenerateAsync(
            Context(), new GenerateIReTourSingleSessionReportRequestDto(),
            "https://reports.example.test", "request", CancellationToken.None);

        Assert.Equal(ToolStatuses.Unavailable, response.Status);
        Assert.Equal(IReTourContract.Version, response.ContractVersion);
        Assert.Contains(reason, response.ReasonCodes);
        Assert.Null(response.Data);
        Assert.Equal(0, renderer.CallCount);
    }

    [Fact]
    public async Task Single_report_returns_rendering_failure_and_allows_retry()
    {
        var renderer = new FakeRenderer { Failure = new InvalidOperationException("browser unavailable") };
        var service = SingleService(new FakeSessionAnalysisService(), renderer);
        var request = new GenerateIReTourSingleSessionReportRequestDto();
        var failed = await service.GenerateAsync(
            Context(), request, "https://reports.example.test", "first", CancellationToken.None);
        Assert.Equal(ToolStatuses.Failed, failed.Status);
        Assert.Contains(IReTourReasonCodes.ReportRenderingUnavailable, failed.ReasonCodes);
        Assert.Null(failed.Data);

        renderer.Failure = null;
        var retried = await service.GenerateAsync(
            Context(), request, "https://reports.example.test", "second", CancellationToken.None);
        Assert.Equal(ToolStatuses.Available, retried.Status);
        Assert.Equal(2, renderer.CallCount);
    }

    [Fact]
    public async Task Single_report_preserves_request_cancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var renderer = new FakeRenderer { Failure = new OperationCanceledException(cancellation.Token) };
        var service = SingleService(new FakeSessionAnalysisService(), renderer);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.GenerateAsync(
            Context(), new GenerateIReTourSingleSessionReportRequestDto(),
            "https://reports.example.test", "request", cancellation.Token));
    }

    [Fact]
    public async Task Single_report_uses_configured_public_url_and_checks_storage_configuration()
    {
        var options = ImageOptions();
        options.ReportImage.PublicBaseUrl = " https://public.example.test/reports/ ";
        var renderer = new FakeRenderer();
        var service = SingleService(new FakeSessionAnalysisService(), renderer, options);
        var response = await service.GenerateAsync(
            Context(), new GenerateIReTourSingleSessionReportRequestDto(),
            "http://internal:5000", "request", CancellationToken.None);
        Assert.StartsWith("https://public.example.test/reports/api/ai/patients/", response.Data!.ImageUrl);

        options.ReportImage.SigningKey = string.Empty;
        service = SingleService(new FakeSessionAnalysisService(), renderer, options);
        response = await service.GenerateAsync(
            Context(), new GenerateIReTourSingleSessionReportRequestDto(),
            "http://internal:5000", "request", CancellationToken.None);
        Assert.Equal(ToolStatuses.Failed, response.Status);
        Assert.Contains(IReTourReasonCodes.ReportImageSigningKeyNotConfigured, response.ReasonCodes);
        Assert.Equal(1, renderer.CallCount);
    }

    [Theory]
    [InlineData("metric")]
    [InlineData("direction")]
    public async Task Longitudinal_report_reuses_content_and_refreshes_changed_summary(string field)
    {
        var imageOptions = ImageOptions();
        var analysis = new FakeLongitudinalAnalysisService(3);
        var renderer = new FakeRenderer();
        var service = new IReTourLongitudinalReportService(
            analysis, new FakeLongitudinalHtmlBuilder(), renderer,
            new EphemeralIReGoReportArtifactStore(Options.Create(imageOptions)),
            Options.Create(imageOptions), Options.Create(TourOptions()),
            NullLogger<IReTourLongitudinalReportService>.Instance);
        var request = new GenerateIReTourLongitudinalReportRequestDto { ReportCount = 3 };
        var first = await service.GenerateAsync(
            Context(), request, "https://reports.example.test", "first", CancellationToken.None);
        var repeated = await service.GenerateAsync(
            Context(), request, "https://reports.example.test", "repeated", CancellationToken.None);
        Assert.Equal(first.Data!.ArtifactRef, repeated.Data!.ArtifactRef);
        Assert.Equal(1, renderer.CallCount);

        if (field == "metric") analysis.MetricName = "更新后的指标名称";
        if (field == "direction") analysis.Direction = "fluctuating";
        var changed = await service.GenerateAsync(
            Context(), request, "https://reports.example.test", "changed", CancellationToken.None);
        Assert.NotEqual(first.Data.ArtifactRef, changed.Data!.ArtifactRef);
        Assert.Equal(IReTourContract.Version, changed.ContractVersion);
        Assert.Equal(2, renderer.CallCount);
    }

    private static IReTourSingleSessionReportService SingleService(
        FakeSessionAnalysisService analysis, FakeRenderer renderer, IReGoToolOptions? imageOptions = null)
    {
        imageOptions ??= ImageOptions();
        return new IReTourSingleSessionReportService(
            analysis, new FakeSingleHtmlBuilder(), renderer,
            new EphemeralIReGoReportArtifactStore(Options.Create(imageOptions)),
            Options.Create(imageOptions), Options.Create(TourOptions()),
            NullLogger<IReTourSingleSessionReportService>.Instance);
    }

    [Fact]
    public async Task Single_report_is_rendered_once_and_returns_stable_artifact()
    {
        var imageOptions = ImageOptions();
        var tourOptions = TourOptions();
        var renderer = new FakeRenderer();
        var service = new IReTourSingleSessionReportService(
            new FakeSessionAnalysisService(),
            new FakeSingleHtmlBuilder(),
            renderer,
            new EphemeralIReGoReportArtifactStore(Options.Create(imageOptions)),
            Options.Create(imageOptions),
            Options.Create(tourOptions),
            NullLogger<IReTourSingleSessionReportService>.Instance);

        var first = await service.GenerateAsync(
            Context(),
            new GenerateIReTourSingleSessionReportRequestDto
            {
                SessionRef = "iretour_s_v1_test"
            },
            "https://reports.example.test",
            "request-1",
            CancellationToken.None);
        var second = await service.GenerateAsync(
            Context(),
            new GenerateIReTourSingleSessionReportRequestDto
            {
                SessionRef = "iretour_s_v1_test"
            },
            "https://reports.example.test",
            "request-2",
            CancellationToken.None);

        Assert.Equal(ToolStatuses.Available, first.Status);
        Assert.Equal(first.Data!.ArtifactRef, second.Data!.ArtifactRef);
        Assert.StartsWith(
            "https://reports.example.test/api/ai/patients/report-artifacts/",
            first.Data.ImageUrl);
        Assert.EndsWith("/content", first.Data.ImageUrl);
        Assert.Equal("iretour-single-v1", first.Data.TemplateVersion);
        Assert.Equal(1, renderer.CallCount);
    }

    [Fact]
    public async Task Single_report_rejects_incomplete_training()
    {
        var imageOptions = ImageOptions();
        var renderer = new FakeRenderer();
        var service = new IReTourSingleSessionReportService(
            new FakeSessionAnalysisService("interrupted"),
            new FakeSingleHtmlBuilder(),
            renderer,
            new EphemeralIReGoReportArtifactStore(Options.Create(imageOptions)),
            Options.Create(imageOptions),
            Options.Create(TourOptions()),
            NullLogger<IReTourSingleSessionReportService>.Instance);

        var response = await service.GenerateAsync(
            Context(),
            new GenerateIReTourSingleSessionReportRequestDto
            {
                SessionRef = "iretour_s_v1_test"
            },
            "https://reports.example.test",
            "request-1",
            CancellationToken.None);

        Assert.Equal(ToolStatuses.Unavailable, response.Status);
        Assert.Contains(
            IReTourReasonCodes.SingleReportRequiresCompletedSession,
            response.ReasonCodes);
        Assert.Equal(0, renderer.CallCount);
    }

    [Fact]
    public async Task Longitudinal_report_requires_three_valid_metric_points()
    {
        var imageOptions = ImageOptions();
        var renderer = new FakeRenderer();
        var service = new IReTourLongitudinalReportService(
            new FakeLongitudinalAnalysisService(validPointCount: 2),
            new FakeLongitudinalHtmlBuilder(),
            renderer,
            new EphemeralIReGoReportArtifactStore(Options.Create(imageOptions)),
            Options.Create(imageOptions),
            Options.Create(TourOptions()),
            NullLogger<IReTourLongitudinalReportService>.Instance);

        var response = await service.GenerateAsync(
            Context(),
            new GenerateIReTourLongitudinalReportRequestDto
            {
                ReportCount = 3,
                ActivityScope = "straight_primary"
            },
            "https://reports.example.test",
            "request-1",
            CancellationToken.None);

        Assert.Equal(ToolStatuses.Unavailable, response.Status);
        Assert.Contains(
            IReTourReasonCodes.LongitudinalReportInsufficientData,
            response.ReasonCodes);
        Assert.Equal(0, renderer.CallCount);
    }

    [Fact]
    public async Task Longitudinal_report_is_rendered_for_three_valid_points()
    {
        var imageOptions = ImageOptions();
        var renderer = new FakeRenderer();
        var service = new IReTourLongitudinalReportService(
            new FakeLongitudinalAnalysisService(validPointCount: 3),
            new FakeLongitudinalHtmlBuilder(),
            renderer,
            new EphemeralIReGoReportArtifactStore(Options.Create(imageOptions)),
            Options.Create(imageOptions),
            Options.Create(TourOptions()),
            NullLogger<IReTourLongitudinalReportService>.Instance);

        var response = await service.GenerateAsync(
            Context(),
            new GenerateIReTourLongitudinalReportRequestDto
            {
                ReportCount = 3,
                ActivityScope = "straight_primary"
            },
            "https://reports.example.test",
            "request-1",
            CancellationToken.None);

        Assert.Equal(ToolStatuses.Available, response.Status);
        Assert.Equal("iretour-longitudinal-v1", response.Data!.TemplateVersion);
        Assert.Contains("![IReTour多次训练报告对比]", response.Data.DisplayMarkdown);
        Assert.Equal(1, renderer.CallCount);
    }

    private static IReGoToolOptions ImageOptions() => new()
    {
        ReportImage = new IReGoReportImageOptions
        {
            SigningKey = new string('r', 40),
            MaxImageBytes = 1024 * 1024,
            MaxCacheBytes = 2 * 1024 * 1024
        }
    };

    private static IReTourToolOptions TourOptions() => new()
    {
        SingleReportTemplateVersion = "iretour-single-v1",
        LongitudinalReportTemplateVersion = "iretour-longitudinal-v1"
    };

    private static TrustedPatientContext Context() => new()
    {
        PatientId = 7,
        SubjectRef = "dify_user:7"
    };

    private sealed class FakeSessionAnalysisService : IIReTourSessionAnalysisService
    {
        private readonly string _trainingState;
        public string ActivityName { get; set; } = "直行训练";
        public string ParameterName { get; set; } = "起始方向";
        public string MetricName { get; set; } = "平均速度";
        public bool Usable { get; set; } = true;
        public string ResultState { get; set; } = "summary_available";
        public string ValueStatus { get; set; } = "valid";

        public FakeSessionAnalysisService(string trainingState = "completed")
        {
            _trainingState = trainingState;
        }

        public Task<ToolResponseDto<IReTourSessionAnalysisDto>> GetAsync(
            TrustedPatientContext context,
            GetIReTourSessionAnalysisRequestDto request,
            string requestId,
            CancellationToken cancellationToken) =>
            Task.FromResult(ToolResponseDto<IReTourSessionAnalysisDto>.Create(
                "get_iretour_session_analysis",
                requestId,
                ToolStatuses.Success,
                new IReTourSessionAnalysisDto
                {
                    Session = new IReTourSessionIdentityDto
                    {
                        PatientRef = context.PatientRef,
                        SessionRef = request.SessionRef ?? string.Empty
                    },
                    Time = new IReTourSessionTimeDto
                    {
                        TrainingState = _trainingState,
                        ExecutionStartedAt = DateTimeOffset.Parse(
                            "2026-01-01T09:00:00+08:00"),
                        ExecutionEndedAt = DateTimeOffset.Parse(
                            "2026-01-01T09:02:00+08:00")
                    },
                    Activity = new IReTourActivityBriefDto
                    {
                        CanonicalCode = "straight_primary",
                        DisplayName = ActivityName
                    },
                    Quality = new IReTourSessionQualityDto { Usable = Usable },
                    Plan = new IReTourSessionPlanDto
                    {
                        Parameters = new[]
                        {
                            new IReTourPlanParameterDto
                            {
                                Code = "start_from_first", DisplayName = ParameterName, Value = "true"
                            }
                        }
                    },
                    Result = new IReTourSessionResultDto
                    {
                        ResultState = ResultState,
                        Metrics = new[]
                        {
                            new IReTourMetricDto
                            {
                                MetricCode = "straight_speed",
                                DisplayName = MetricName,
                                NormalizedValue = 0.5m,
                                ValueStatus = ValueStatus
                            }
                        }
                    }
                },
                Array.Empty<string>(),
                new ToolMetaDto()));
    }

    private sealed class FakeLongitudinalAnalysisService :
        IIReTourLongitudinalAnalysisService
    {
        private readonly int _validPointCount;
        public string MetricName { get; set; } = "平均速度";
        public string Direction { get; set; } = "increased";

        public FakeLongitudinalAnalysisService(int validPointCount)
        {
            _validPointCount = validPointCount;
        }

        public Task<ToolResponseDto<IReTourLongitudinalAnalysisDto>> GetAsync(
            TrustedPatientContext context,
            GetIReTourLongitudinalAnalysisRequestDto request,
            string requestId,
            CancellationToken cancellationToken)
        {
            var sessions = Enumerable.Range(1, 3)
                .Select(index => new IReTourLongitudinalSessionDto
                {
                    Ordinal = index,
                    SessionRef = $"iretour_s_v1_{index}",
                    SessionTime = DateTimeOffset.Parse(
                        $"2026-01-0{index}T09:00:00+08:00"),
                    CanonicalCode = request.ActivityScope,
                    TrainingState = "completed",
                    ResultState = "summary_available"
                })
                .ToArray();
            var points = Enumerable.Range(1, 3)
                .Select(index => new IReTourMetricPointDto
                {
                    Ordinal = index,
                    SessionRef = sessions[index - 1].SessionRef,
                    SessionTime = sessions[index - 1].SessionTime,
                    Value = index <= _validPointCount ? index : null,
                    ValueStatus = index <= _validPointCount ? "valid" : "missing"
                })
                .ToArray();
            return Task.FromResult(
                ToolResponseDto<IReTourLongitudinalAnalysisDto>.Create(
                    "get_iretour_longitudinal_analysis",
                    requestId,
                    ToolStatuses.Success,
                    new IReTourLongitudinalAnalysisDto
                    {
                        PatientRef = context.PatientRef,
                        Window = new IReTourLongitudinalWindowDto
                        {
                            ResolvedCount = 3,
                            ComparableCount = 1
                        },
                        Sessions = sessions,
                        MetricSeries = new[]
                        {
                            new IReTourMetricSeriesDto
                            {
                                MetricCode = "straight_speed",
                                DisplayName = MetricName,
                                Direction = Direction,
                                Points = points
                            }
                        }
                    },
                    Array.Empty<string>(),
                    new ToolMetaDto()));
        }
    }

    private sealed class FakeSingleHtmlBuilder :
        IIReTourSingleSessionReportHtmlBuilder
    {
        public string Build(IReTourSessionAnalysisDto analysis) =>
            "<html><body>single</body></html>";
    }

    private sealed class FakeLongitudinalHtmlBuilder :
        IIReTourLongitudinalReportHtmlBuilder
    {
        public string Build(IReTourLongitudinalAnalysisDto analysis) =>
            "<html><body>longitudinal</body></html>";
    }

    private sealed class FakeRenderer : IIReGoReportImageRenderer
    {
        public int CallCount { get; private set; }
        public Exception? Failure { get; set; }

        public Task<byte[]> RenderPngAsync(
            string html,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            if (Failure != null) throw Failure;
            return Task.FromResult(new byte[] { 137, 80, 78, 71 });
        }
    }
}
