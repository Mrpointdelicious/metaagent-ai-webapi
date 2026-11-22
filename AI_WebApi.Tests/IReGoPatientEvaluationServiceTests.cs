using AI_WebApi.Model.AITools;
using AI_WebApi.Model.Domain.IReGo;
using AI_WebApi.Model.Entities;
using AI_WebApi.Repository.AITools;
using AI_WebApi.Services.AITools.IReGo;

namespace AI_WebApi.Tests;

public sealed class IReGoPatientEvaluationServiceTests
{
    [Fact]
    public async Task Robotdb_summary_report_is_used_without_revision_selection()
    {
        var raw = DataSet(
            modelId: 1,
            new RehabReportEntity { Id = 1, PlanId = 1, ReportDetails = ValidReport });
        var service = Service(raw);

        var result = await service.EvaluateAsync(Context());

        Assert.Equal(1, result.StrictPlanCount);
        Assert.Equal(1, result.StrictReportCount);
        Assert.Single(result.Sessions);
        Assert.Equal(1, result.Sessions[0].Report.Id);
        Assert.True(result.Sessions[0].Usable);
        Assert.DoesNotContain(
            result.Issues,
            x => x.Code == IReGoReasonCodes.MultipleReportsLatestSelected);
    }

    [Fact]
    public async Task Report_time_before_source_record_creation_is_not_latest_eligible()
    {
        var raw = DataSet(
            modelId: 1,
            new RehabReportEntity
            {
                Id = 1,
                PlanId = 1,
                ReportDetails = ValidReport,
                CreateTime = new DateTime(2026, 7, 28, 12, 0, 0),
            });
        raw.Plans.Single().CreateTime = new DateTime(2026, 7, 29, 12, 0, 0);

        var result = await Service(raw).EvaluateAsync(Context());

        Assert.Single(result.Sessions);
        Assert.False(result.Sessions[0].EligibleForLatest);
        Assert.Equal(1, result.StrictReportCount);
        Assert.Contains(
            result.Sessions[0].Issues,
            x => x.Code == IReGoReasonCodes.ReportTimeBeforeSourceRecordCreated);
    }

    [Fact]
    public async Task Legacy_void_status_does_not_override_robotdb_complete_status()
    {
        var raw = DataSet(
            modelId: 1,
            new RehabReportEntity
            {
                Id = 10,
                PlanId = 1,
                ReportDetails = ValidReport
            });
        raw.Plans.Single().Status = -1;

        var result = await Service(raw).EvaluateAsync(Context());

        Assert.Equal(1, result.StrictPlanCount);
        Assert.Equal(1, result.EligiblePlanCount);
        Assert.Equal(0, result.VoidedPlanCount);
        Assert.Single(result.Sessions);
        Assert.Single(result.Plans);
        Assert.False(result.Plans[0].IsVoided);
    }

    [Fact]
    public async Task Robotdb_model_zero_is_not_strict_irego()
    {
        var service = Service(
            DataSet(
                modelId: 0,
                new RehabReportEntity
                {
                    Id = 10,
                    PlanId = 1,
                    ReportDetails = ValidReport
                }));

        var result = await service.EvaluateAsync(Context());

        Assert.Equal(0, result.StrictPlanCount);
        Assert.Empty(result.Sessions);
        Assert.Contains(
            result.Issues,
            x => x.Code == IReGoReasonCodes.DeviceClassificationMismatch);
    }

    [Fact]
    public async Task Structurally_valid_report_is_directly_usable()
    {
        var service = Service(
            DataSet(
                modelId: 1,
                new RehabReportEntity
                {
                    Id = 10,
                    PlanId = 1,
                    ReportDetails = ValidReport
                }));

        var result = await service.EvaluateAsync(Context());

        Assert.Single(result.Sessions);
        Assert.True(result.Sessions[0].Usable);
        Assert.Equal("structurally_valid", result.Sessions[0].ValidationStatus);
        Assert.Equal("supported_session_available", result.DataState);
    }

    [Fact]
    public async Task Invalid_report_is_not_usable()
    {
        var service = Service(
            DataSet(
                modelId: 1,
                new RehabReportEntity
                {
                    Id = 10,
                    PlanId = 1,
                    ReportDetails = "not-json"
                }));

        var result = await service.EvaluateAsync(Context());

        Assert.Single(result.Sessions);
        Assert.False(result.Sessions[0].Usable);
        Assert.Equal("invalid", result.Sessions[0].ValidationStatus);
        Assert.Equal("report_invalid", result.DataState);
    }

    private const string ValidReport =
        @"{""ReportList"":[{""ReportMode"":1,""WDistance"":0}],""ReportProcess"":""1""}";

    private static IReGoPatientEvaluationService Service(
        IReGoRawDataSet data) =>
        new(
            new FakeRepository(data),
            new IReGoReportParser());

    private static TrustedPatientContext Context() => new()
    {
        PatientId = 7,
        SubjectRef = "dify_user:7"
    };

    private static IReGoRawDataSet DataSet(
        int modelId,
        params RehabReportEntity[] reports) => new()
        {
            Patient = new PatientEntity
            {
                Id = 7,
                BusinessId = 3,
                IsDeleted = false
            },
            Plans = new[]
            {
                new RehabPlanEntity
                {
                    Id = 1,
                    UserId = 7,
                    BusinessId = 3,
                    DeviceId = 2,
                    TemplateId = 4,
                    IsComplete = 1
                }
            },
            Devices = new Dictionary<int, DeviceEntity>
            {
                [2] = new() { Id = 2, DeviceType = 1 }
            },
            Templates = new Dictionary<int, RehabTemplateEntity>
            {
                [4] = new() { Id = 4, ModelId = modelId }
            },
            Reports = reports
        };

    private sealed class FakeRepository : IIReGoDataRepository
    {
        private readonly IReGoRawDataSet _data;

        public FakeRepository(IReGoRawDataSet data)
        {
            _data = data;
        }

        public Task<IReGoRawDataSet> LoadPatientDataAsync(
            TrustedPatientContext context,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_data);
    }
}
