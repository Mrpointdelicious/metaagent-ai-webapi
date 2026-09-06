using AI_WebApi.IServices.AITools;
using AI_WebApi.Model.AITools;
using AI_WebApi.Model.DTOs.AITools;
using AI_WebApi.Services.AITools.HospitalOperations;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AI_WebApi.Tests;

public sealed class HospitalOperationsServiceTests
{
    [Fact]
    public async Task Agent_selected_id_is_revalidated_against_directory()
    {
        using var context = new ServiceContext();

        var response = await context.Service.QueryAsync(
            new HospitalOperationsQueryRequestDto
            {
                HospitalId = 999,
                HospitalName = "编造医院"
            },
            "http://localhost:5043",
            "request-1");

        Assert.Equal("invalid_request", response.Status);
        Assert.Contains(
            HospitalOperationsReasonCodes.HospitalNotFound,
            response.ReasonCodes);
    }

    [Fact]
    public async Task Generates_durable_partial_report_with_stable_urls()
    {
        using var context = new ServiceContext();

        var response = await context.Service.QueryAsync(
            ReportRequest("rooms"),
            "http://localhost:5043",
            "request-2");

        Assert.Equal("success", response.Status);
        var report = Assert.IsType<HospitalOperationsReportArtifactDto>(
            response.Data?.Report);
        Assert.StartsWith("hospital_report_v2_", report.ArtifactRef);
        Assert.Contains("/report-artifacts/", report.ImageUrl);
        Assert.EndsWith("?format=png", report.ImageUrl);
        Assert.EndsWith("?format=html", report.HtmlUrl);
        Assert.Null(report.ExpiresAt);
        Assert.False(report.Reused);
        Assert.Equal("durable_filesystem", report.StorageMode);
        Assert.NotNull(response.Data!.Hospitals.Single().Rooms);
        Assert.Null(response.Data.Hospitals.Single().Overview);
    }

    [Fact]
    public async Task Repeated_report_reuses_artifact_without_rerendering()
    {
        using var context = new ServiceContext();
        var request = ReportRequest("all");

        var first = await context.Service.QueryAsync(
            request,
            "http://localhost:5043",
            "request-first");
        var second = await context.Service.QueryAsync(
            request,
            "http://localhost:5043",
            "request-second");

        Assert.Equal(
            first.Data!.Report!.ArtifactRef,
            second.Data!.Report!.ArtifactRef);
        Assert.False(first.Data.Report.Reused);
        Assert.True(second.Data.Report.Reused);
        Assert.Equal(1, context.Renderer.CallCount);
    }

    [Fact]
    public async Task In_place_metric_change_creates_new_artifact()
    {
        using var context = new ServiceContext();
        var request = ReportRequest("all");

        var first = await context.Service.QueryAsync(
            request,
            "http://localhost:5043",
            "request-first");
        context.Upstream.PatientCount = 63;
        var changed = await context.Service.QueryAsync(
            request,
            "http://localhost:5043",
            "request-changed");

        Assert.NotEqual(
            first.Data!.Report!.ArtifactRef,
            changed.Data!.Report!.ArtifactRef);
        Assert.NotEqual(
            first.Data.Report.ContentFingerprint,
            changed.Data.Report.ContentFingerprint);
        Assert.Equal(2, context.Renderer.CallCount);
    }

    [Fact]
    public async Task Comparison_hospital_input_order_is_normalized()
    {
        using var context = new ServiceContext();
        var firstRequest = ComparisonRequest(new[] { 25, 13 });
        var secondRequest = ComparisonRequest(new[] { 13, 25 });

        var first = await context.Service.QueryAsync(
            firstRequest,
            "http://localhost:5043",
            "request-first");
        var second = await context.Service.QueryAsync(
            secondRequest,
            "http://localhost:5043",
            "request-second");

        Assert.Equal(
            first.Data!.Report!.ArtifactRef,
            second.Data!.Report!.ArtifactRef);
        Assert.Equal(new[] { 13, 25 }, first.Data.Hospitals
            .Select(x => x.Hospital.HospitalId));
        var comparison = Assert.IsType<HospitalOperationsPairwiseComparisonDto>(
            first.Data.Comparison);
        Assert.Equal(13, comparison.LeftHospital.HospitalId);
        Assert.Equal(25, comparison.RightHospital.HospitalId);
        Assert.Equal("right_minus_left", comparison.DifferenceDefinition);
        var patientMetric = Assert.Single(comparison.Metrics.Where(
            x => x.Code == "registered_patients"));
        Assert.Equal(75m, patientMetric.LeftValue);
        Assert.Equal(87m, patientMetric.RightValue);
        Assert.Equal(12m, patientMetric.Difference);
        Assert.Equal(25, patientMetric.LargerValueHospitalId);
        Assert.Equal(1, context.Renderer.CallCount);
    }

    [Theory]
    [InlineData(new[] { 13 })]
    [InlineData(new[] { 13, 13 })]
    public async Task Comparison_requires_exactly_two_distinct_hospitals(
        int[] ids)
    {
        using var context = new ServiceContext();

        var response = await context.Service.QueryAsync(
            ComparisonRequest(ids),
            "http://localhost:5043",
            "request-invalid-comparison");

        Assert.Equal("invalid_request", response.Status);
        Assert.Contains(
            HospitalOperationsReasonCodes.ComparisonRequiresExactlyTwoHospitals,
            response.ReasonCodes);
        Assert.Contains("只能选择两家医院", response.AgentSummary);
        Assert.Equal(0, context.Renderer.CallCount);
    }

    [Fact]
    public async Task Report_generation_returns_clear_error_when_key_is_missing()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "ai-webapi-hospital-service-tests",
            Guid.NewGuid().ToString("N"));
        try
        {
            var options = Options.Create(new HospitalOperationsOptions
            {
                ArtifactStoragePath = path,
                MaxArtifactBytes = 1024 * 1024
            });
            var renderer = new FakeRenderer();
            var service = new HospitalOperationsService(
                new FakeUpstream(),
                new HospitalOperationsReportHtmlBuilder(),
                renderer,
                new HospitalOperationsArtifactIdentityProvider(options),
                new FileSystemHospitalOperationsArtifactStore(options),
                options,
                NullLogger<HospitalOperationsService>.Instance);

            var response = await service.QueryAsync(
                ReportRequest("all"),
                "http://localhost:5043",
                "request-missing-key");

            Assert.Equal("failed", response.Status);
            Assert.Contains(
                HospitalOperationsReasonCodes.ReportArtifactNotConfigured,
                response.ReasonCodes);
            Assert.Equal(0, renderer.CallCount);
        }
        finally
        {
            var root = Path.GetFullPath(Path.Combine(
                Path.GetTempPath(),
                "ai-webapi-hospital-service-tests"));
            var target = Path.GetFullPath(path);
            if (target.StartsWith(root + Path.DirectorySeparatorChar) &&
                Directory.Exists(target))
            {
                Directory.Delete(target, true);
            }
        }
    }

    private static HospitalOperationsQueryRequestDto ReportRequest(
        string section) => new()
        {
            HospitalId = 13,
            HospitalName = "上海中医药大学附属岳阳医院",
            Section = section,
            OutputMode = "analysis_and_report",
            StartDate = "2026-08-01",
            EndDate = "2026-08-31"
        };

    private static HospitalOperationsQueryRequestDto ComparisonRequest(
        IReadOnlyList<int> ids) => new()
        {
            Scope = "comparison",
            HospitalIds = ids,
            Section = "all",
            OutputMode = "analysis_and_report",
            StartDate = "2026-08-01",
            EndDate = "2026-08-31"
        };

    private sealed class ServiceContext : IDisposable
    {
        private readonly string _path = Path.Combine(
            Path.GetTempPath(),
            "ai-webapi-hospital-service-tests",
            Guid.NewGuid().ToString("N"));

        public ServiceContext()
        {
            var options = Options.Create(new HospitalOperationsOptions
            {
                ArtifactRefSigningKey = new string('h', 40),
                ArtifactStoragePath = _path,
                MaxArtifactBytes = 1024 * 1024,
                AnalysisRuleVersion = "hospital-analysis-rules-v2",
                TemplateVersion = "hospital-operations-v3"
            });
            Upstream = new FakeUpstream();
            Renderer = new FakeRenderer();
            Service = new HospitalOperationsService(
                Upstream,
                new HospitalOperationsReportHtmlBuilder(),
                Renderer,
                new HospitalOperationsArtifactIdentityProvider(options),
                new FileSystemHospitalOperationsArtifactStore(options),
                options,
                NullLogger<HospitalOperationsService>.Instance);
        }

        public FakeUpstream Upstream { get; }
        public FakeRenderer Renderer { get; }
        public HospitalOperationsService Service { get; }

        public void Dispose()
        {
            var root = Path.GetFullPath(Path.Combine(
                Path.GetTempPath(),
                "ai-webapi-hospital-service-tests"));
            var target = Path.GetFullPath(_path);
            if (target.StartsWith(root + Path.DirectorySeparatorChar) &&
                Directory.Exists(target))
            {
                Directory.Delete(target, true);
            }
        }
    }

    internal sealed class FakeRenderer : IIReGoReportImageRenderer
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

    internal sealed class FakeUpstream : IHospitalOperationsUpstreamClient
    {
        public int PatientCount { get; set; } = 62;

        public Task<IReadOnlyList<HospitalDirectoryEntryDto>> GetDirectoryAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<HospitalDirectoryEntryDto>>(new[]
            {
                new HospitalDirectoryEntryDto
                {
                    HospitalId = 13,
                    HospitalName = "上海中医药大学附属岳阳医院",
                    TenantLevel = 3
                },
                new HospitalDirectoryEntryDto
                {
                    HospitalId = 25,
                    HospitalName = "上海浦东医院",
                    TenantLevel = 3
                }
            });

        public Task<HospitalOverviewDto> GetOverviewAsync(
            int hospitalId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new HospitalOverviewDto
            {
                PatientCount = PatientCount + hospitalId,
                DoctorCount = 46,
                DailyActiveCount = 1,
                PlatformHospitalCount = 45,
                PlatformSchoolCount = 4
            });

        public Task<HospitalConsultationDto> GetConsultationAsync(
            int hospitalId,
            DateOnly startDate,
            DateOnly endDate,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new HospitalConsultationDto
            {
                ConsultationPeople = 49 + hospitalId,
                ConsultationTimes = 176 + hospitalId
            });

        public Task<HospitalRoomsDto> GetRoomsAsync(
            int hospitalId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new HospitalRoomsDto
            {
                ConsultationRooms = 8,
                AssessmentRooms = 8,
                TrainingRooms = 8
            });

        public Task<(HospitalAssessmentTrainingDto AssessmentTraining,
            HospitalRecordsDto Records)> GetDetailsAsync(
            int hospitalId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult((
                new HospitalAssessmentTrainingDto(),
                new HospitalRecordsDto
                {
                    RealConsultationCount = 79,
                    MedicalRecordCount = 181,
                    PrescriptionTemplateCount = 90,
                    ReportCount = 90
                }));
    }
}
