using AI_WebApi.IServices.AITools;
using AI_WebApi.Model.AITools;
using AI_WebApi.Model.Domain.IReGo;
using AI_WebApi.Model.DTOs.AITools;
using AI_WebApi.Model.Entities;
using AI_WebApi.Services.AITools.IReGo;
using AI_WebApi.Services.AITools.PatientContext;
using Microsoft.Extensions.Options;

namespace AI_WebApi.Tests;

public sealed class IReGoSessionAnalysisServiceTests
{
    [Fact]
    public async Task Latest_reference_is_stable_across_service_and_cache_recreation()
    {
        var evaluation = Evaluation(patientId: 461, reportId: 2002);
        var context = Context(461);

        var first = await SessionService(evaluation).GetAsync(
            context,
            LatestRequest(),
            "request-1");
        var second = await SessionService(evaluation).GetAsync(
            context,
            LatestRequest(),
            "request-2");

        Assert.Equal(
            first.Data!.Session.SessionRef,
            second.Data!.Session.SessionRef);
    }

    [Fact]
    public async Task Session_reference_resolves_without_cache_warmup()
    {
        var evaluation = Evaluation(patientId: 461, reportId: 2002);
        var context = Context(461);
        var session = evaluation.Sessions.Single();
        var sessionRef = IReGoSessionRefProviderTests.Provider().Create(
            evaluation.Raw.Patient!.BusinessId,
            context.PatientId,
            session.Plan.Id,
            session.Report.Id,
            session.ParsedReport.SchemaSignature);

        var response = await SessionService(evaluation).GetAsync(
            context,
            ByReferenceRequest(sessionRef),
            "request-1");

        Assert.NotNull(response.Data);
        Assert.Equal(sessionRef, response.Data!.Session.SessionRef);
    }

    [Fact]
    public async Task Patient_cannot_use_another_patients_reference_even_if_cached()
    {
        var evaluation = Evaluation(patientId: 461, reportId: 2002);
        var context = Context(461);
        var provider = IReGoSessionRefProviderTests.Provider();
        var otherPatientRef = provider.Create(
            3,
            462,
            1001,
            3003,
            "rf_other");
        var cache = new InMemoryPatientContextSnapshotCache();
        cache.StoreSession(new IReGoSessionBinding
        {
            SessionRef = otherPatientRef,
            ContextKey = Context(462).ContextKey,
            PlanId = 1001,
            ReportId = 3003,
            SchemaSignature = "rf_other",
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5)
        });

        var response = await SessionService(evaluation, cache).GetAsync(
            context,
            ByReferenceRequest(otherPatientRef),
            "request-1");

        Assert.Null(response.Data);
        Assert.Contains(
            IReGoReasonCodes.SessionRefNotFound,
            response.ReasonCodes);
    }

    [Fact]
    public async Task Invalid_reference_returns_session_ref_not_found()
    {
        var response = await SessionService(Evaluation(461, 2002)).GetAsync(
            Context(461),
            ByReferenceRequest("irego_s_v1_invalid"),
            "request-1");

        Assert.Equal(ToolStatuses.Failed, response.Status);
        Assert.Contains(
            IReGoReasonCodes.SessionRefNotFound,
            response.ReasonCodes);
    }

    [Fact]
    public async Task Patient_461_shape_returns_three_named_walking_blocks_and_seconds()
    {
        var response = await SessionService(Evaluation(461, 2002)).GetAsync(
            Context(461),
            LatestRequest(),
            "request-1");

        Assert.NotNull(response.Data);
        Assert.Equal(3, response.Data!.Overview.ReportItemCount);
        Assert.Equal(3, response.Data.Overview.ReportBlockCount);
        Assert.Equal("报告记录分段数", response.Data.Overview.ReportBlockLabel);
        Assert.Equal(263m, response.Data.Overview.TotalTrainingDuration);
        Assert.Equal("s", response.Data.Overview.DurationUnit);
        Assert.Equal(
            "263秒（约4分23秒）",
            response.Data.Overview.TotalTrainingDurationDisplay);
        Assert.Equal(
            new[] { "步行训练第1段", "步行训练第2段", "步行训练第3段" },
            response.Data.DirectTraining.Select(x => x.DisplayName));
        Assert.Equal(new[] { 1, 2, 3 }, response.Data.DirectTraining.Select(x => x.Sequence));
    }

    [Fact]
    public async Task Context_tool_returns_same_latest_reference_after_service_recreation()
    {
        var evaluation = Evaluation(461, 2002);
        var context = Context(461);
        var request = new GetMultisourcePatientContextRequestDto
        {
            ForceRefresh = true
        };

        var first = await ContextService(evaluation).GetAsync(
            context,
            request,
            "request-1");
        var second = await ContextService(evaluation).GetAsync(
            context,
            request,
            "request-2");

        Assert.Equal(
            first.Data!.DataDomains.Irego.Latest!.SessionRef,
            second.Data!.DataDomains.Irego.Latest!.SessionRef);
    }

    [Fact]
    public async Task Duration_that_exceeds_plan_window_returns_plain_language_limit()
    {
        const string reportJson = """
            {
              "ReportList": [
                { "ReportMode": 1, "WTrainingWalktime": 300 }
              ],
              "ReportProcess": "1"
            }
            """;
        var response = await SessionService(
                Evaluation(461, 2002, reportJson, planWindowSeconds: 30))
            .GetAsync(
                Context(461),
                LatestRequest(),
                "request-1");

        var issue = Assert.Single(response.Data!.Quality.Issues.Where(x =>
            x.Code == IReGoReasonCodes.ReportDurationExceedsPlanWindow));
        Assert.False(string.IsNullOrWhiteSpace(issue.PatientMessage));
    }

    [Fact]
    public async Task Session_analysis_adds_plan_report_blocks_and_metric_comparison()
    {
        const string planJson = """
            [
              {
                "ReportMode": 1,
                "speed": 0.8,
                "assistance": 2
              }
            ]
            """;
        const string reportJson = """
            {
              "ReportList": [
                {
                  "ReportMode": 1,
                  "WAvgSpeed": 0.7,
                  "WalkReportList": [
                    { "Assistance": 2 }
                  ]
                }
              ],
              "ReportProcess": "1"
            }
            """;

        var response = await SessionService(
                Evaluation(
                    461,
                    2002,
                    reportJson,
                    planDetails: planJson))
            .GetAsync(
                Context(461),
                LatestRequest(),
                "request-1");

        Assert.Equal("1.6.0", response.ContractVersion);
        Assert.Single(response.Data!.Plan.Blocks);
        Assert.Single(response.Data.ReportBlocks);
        Assert.Single(response.Data.DirectTraining);
        var comparison = Assert.Single(response.Data.BlockComparison);
        Assert.Equal("matched_exact", comparison.Status);
        var speed = Assert.Single(comparison.MetricComparisons.Where(x =>
            x.PlannedMetricCode == "plan_walking_speed"));
        Assert.True(speed.Comparable);
        Assert.Equal("different", speed.Relation);
        Assert.Equal("m/s", speed.Unit);
        Assert.Equal(100m, response.Data.ReportEvaluationFacts.CompletionRate);
        Assert.Equal("high", response.Data.ReportEvaluationFacts.CompletionLevel);
        Assert.Equal(
            "planned_report_block_match",
            response.Data.ReportEvaluationFacts.CompletionBasis);
        Assert.Equal(1, response.Data.ReportEvaluationFacts.CompletionNumerator);
        Assert.Equal(1, response.Data.ReportEvaluationFacts.CompletionDenominator);
    }

    [Fact]
    public async Task Context_exposes_plan_states_and_recent_session_catalog()
    {
        var response = await ContextService(EvaluationWithPlanStates()).GetAsync(
            Context(461),
            new GetMultisourcePatientContextRequestDto
            {
                ForceRefresh = true,
                ProjectionLevel = "standard"
            },
            "request-1");

        var irego = response.Data!.DataDomains.Irego;
        Assert.True(irego.AvailabilityFlags.HasEligiblePlan);
        Assert.True(irego.AvailabilityFlags.HasUsableSession);
        Assert.True(irego.AvailabilityFlags.HasFuturePlan);
        Assert.True(irego.AvailabilityFlags.HasUnfinishedPlan);
        Assert.True(irego.AvailabilityFlags.HasVoidedPlan);
        Assert.Equal(2, irego.Coverage.EligiblePlanCount);
        Assert.Equal(1, irego.Coverage.EffectiveReportCount);
        Assert.Equal(1, irego.Coverage.PlanOnlyCount);
        Assert.Equal(1, irego.Coverage.VoidedPlanCount);
        Assert.Single(irego.RecentSessions);
        Assert.Equal(1, irego.RecentSessions[0].Ordinal);
        Assert.StartsWith("irego_s_v1_", irego.RecentSessions[0].SessionRef);
        Assert.Single(irego.FuturePlans);
        Assert.Equal("not_complete", irego.FuturePlans[0].CompletionFlag);
        Assert.False(string.IsNullOrWhiteSpace(
            irego.FuturePlans[0].PatientMessage));
        Assert.Single(irego.VoidedPlans);
        Assert.Equal("voided", irego.VoidedPlans[0].State);
        Assert.StartsWith("irego_p_v1_", irego.VoidedPlans[0].PlanRef);
    }

    private static IReGoSessionAnalysisService SessionService(
        IReGoPatientEvaluation evaluation,
        IPatientContextSnapshotCache? cache = null) =>
        new(
            new FakeEvaluationService(evaluation),
            IReGoSessionRefProviderTests.Provider(),
            cache ?? new InMemoryPatientContextSnapshotCache(),
            new IReGoStrokeCapabilityMapper(),
            Options.Create(new IReGoToolOptions
            {
                SessionRefSigningKey =
                    "unit-test-only-persistent-signing-key-32-bytes"
            }));

    private static MultisourcePatientContextService ContextService(
        IReGoPatientEvaluation evaluation) =>
        new(
            new FakeEvaluationService(evaluation),
            IReGoSessionRefProviderTests.Provider(),
            new InMemoryPatientContextSnapshotCache(),
            Options.Create(new IReGoToolOptions
            {
                SessionRefSigningKey =
                    "unit-test-only-persistent-signing-key-32-bytes"
            }));

    private static GetIReGoSessionAnalysisRequestDto LatestRequest() => new()
    {
        Selector = "latest_usable",
        DetailLevel = "standard",
        QualityDetail = "summary"
    };

    private static GetIReGoSessionAnalysisRequestDto ByReferenceRequest(
        string sessionRef) => new()
        {
            Selector = "session_ref",
            SessionRef = sessionRef,
            DetailLevel = "standard",
            QualityDetail = "summary"
        };

    private static TrustedPatientContext Context(int patientId) => new()
    {
        PatientId = patientId,
        SubjectRef = $"dify_user:{patientId}"
    };

    private static IReGoPatientEvaluation Evaluation(
        int patientId,
        int reportId,
        string? reportJson = null,
        int planWindowSeconds = 748,
        string? planDetails = null)
    {
        const string defaultReportJson = """
            {
              "ReportList": [
                { "ReportMode": 1, "WTrainingWalktime": 41 },
                { "ReportMode": 1, "WTrainingWalktime": 180 },
                { "ReportMode": 1, "WTrainingWalktime": 42 }
              ],
              "ReportProcess": "111"
            }
            """;
        reportJson ??= defaultReportJson;
        var parsed = new IReGoReportParser().Parse(reportJson);
        var planCreatedAt = new DateTime(2026, 7, 1, 10, 0, 0);
        var plan = new RehabPlanEntity
        {
            Id = 1001,
            UserId = patientId,
            BusinessId = 3,
            DeviceId = 2,
            TemplateId = 4,
            CreateTime = planCreatedAt,
            EndTime = planCreatedAt.AddSeconds(planWindowSeconds),
            IsComplete = 1,
            Details = planDetails
        };
        var report = new RehabReportEntity
        {
            Id = reportId,
            PlanId = plan.Id,
            ReportDetails = reportJson,
            CreateTime = planCreatedAt.AddSeconds(planWindowSeconds)
        };

        return new IReGoPatientEvaluation
        {
            Raw = new IReGoRawDataSet
            {
                Patient = new PatientEntity
                {
                    Id = patientId,
                    BusinessId = 3,
                    IsDeleted = false
                },
                SourceSystem = "configured_database",
                SourceCompleteness = "configured_database"
            },
            Sessions = new[]
            {
                new IReGoEvaluatedSession
                {
                    Plan = plan,
                    Report = report,
                    Device = new DeviceEntity { Id = 2, DeviceType = 1 },
                    Template = new RehabTemplateEntity { Id = 4, ModelId = 0 },
                    ParsedReport = parsed,
                    State = new TrainingStateResult
                    {
                        State = "completed",
                        Confidence = "high"
                    },
                    Usable = true,
                    ValidationStatus = "structurally_valid",
                    Issues = parsed.Issues
                }
            },
            StrictPlanCount = 1,
            StrictReportCount = 1,
            DataState = "supported_session_available",
            Watermark = "test-watermark"
        };
    }

    private static IReGoPatientEvaluation EvaluationWithPlanStates()
    {
        var parsed = new IReGoReportParser().Parse(
            """
            {
              "ReportList": [
                { "ReportMode": 1, "WTrainingWalktime": 30 }
              ],
              "ReportProcess": "1"
            }
            """);
        var completedPlan = new RehabPlanEntity
        {
            Id = 1001,
            UserId = 461,
            BusinessId = 3,
            DeviceId = 2,
            TemplateId = 4,
            BookingTime = new DateTime(2026, 7, 20, 10, 0, 0),
            CreateTime = new DateTime(2026, 7, 19, 10, 0, 0),
            EndTime = new DateTime(2026, 7, 20, 10, 30, 0),
            IsComplete = 1
        };
        var report = new RehabReportEntity
        {
            Id = 2001,
            PlanId = completedPlan.Id,
            ReportDetails = "{}",
            CreateTime = new DateTime(2026, 7, 20, 10, 31, 0)
        };
        var session = new IReGoEvaluatedSession
        {
            Plan = completedPlan,
            Report = report,
            Device = new DeviceEntity { Id = 2, DeviceType = 1 },
            Template = new RehabTemplateEntity
            {
                Id = 4,
                ModelId = 0,
                Title = "步行训练"
            },
            ParsedReport = parsed,
            State = new TrainingStateResult
            {
                State = "completed",
                Confidence = "high"
            },
            Usable = true,
            ValidationStatus = "structurally_valid"
        };
        var futurePlan = new RehabPlanEntity
        {
            Id = 1002,
            UserId = 461,
            BusinessId = 3,
            DeviceId = 2,
            TemplateId = 4,
            BookingTime = DateTime.Now.AddDays(1),
            CreateTime = DateTime.Now,
            IsComplete = 0
        };
        var voidedPlan = new RehabPlanEntity
        {
            Id = 1003,
            UserId = 461,
            BusinessId = 3,
            DeviceId = 2,
            TemplateId = 4,
            BookingTime = DateTime.Now.AddDays(2),
            CreateTime = DateTime.Now,
            IsComplete = 0,
            Status = -1
        };
        var device = new DeviceEntity { Id = 2, DeviceType = 1 };
        var template = new RehabTemplateEntity
        {
            Id = 4,
            ModelId = 0,
            Title = "IREGO训练计划"
        };

        return new IReGoPatientEvaluation
        {
            Raw = new IReGoRawDataSet
            {
                Patient = new PatientEntity
                {
                    Id = 461,
                    BusinessId = 3,
                    IsDeleted = false
                },
                SourceSystem = "configured_database",
                SourceCompleteness = "configured_database"
            },
            Sessions = new[] { session },
            Plans = new[]
            {
                new IReGoEvaluatedPlan
                {
                    Plan = completedPlan,
                    Template = template,
                    Device = device,
                    State = session.State,
                    EffectiveSession = session
                },
                new IReGoEvaluatedPlan
                {
                    Plan = futurePlan,
                    Template = template,
                    Device = device,
                    State = new TrainingStateResult
                    {
                        State = "not_started",
                        Confidence = "medium"
                    }
                },
                new IReGoEvaluatedPlan
                {
                    Plan = voidedPlan,
                    Template = template,
                    Device = device,
                    State = new TrainingStateResult
                    {
                        State = "voided",
                        Confidence = "high"
                    },
                    IsVoided = true
                }
            },
            StrictPlanCount = 3,
            EligiblePlanCount = 2,
            VoidedPlanCount = 1,
            StrictReportCount = 1,
            DataState = "supported_session_available",
            Watermark = "test-watermark"
        };
    }

    private sealed class FakeEvaluationService : IIReGoPatientEvaluationService
    {
        private readonly IReGoPatientEvaluation _evaluation;

        public FakeEvaluationService(IReGoPatientEvaluation evaluation)
        {
            _evaluation = evaluation;
        }

        public Task<IReGoPatientEvaluation> EvaluateAsync(
            TrustedPatientContext context,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_evaluation);
    }
}
