using AI_WebApi.IServices.AITools;
using AI_WebApi.Model.AITools;
using AI_WebApi.Model.Domain.IReGo;
using AI_WebApi.Model.DTOs.AITools;
using AI_WebApi.Model.Entities;
using AI_WebApi.Services.AITools.IReGo;
using Microsoft.Extensions.Options;

namespace AI_WebApi.Tests;

public sealed class IReGoLongitudinalAnalysisServiceTests
{
    [Fact]
    public async Task Repeated_same_project_segments_are_kept_as_separate_lanes()
    {
        var evaluation = Evaluation(
            Session(1, """
                {"ReportList":[
                  {"ReportMode":1,"WAvgSpeed":0.5},
                  {"ReportMode":1,"WAvgSpeed":0.7}
                ],"ReportProcess":"11"}
                """, planBlockCount: 2),
            Session(2, """
                {"ReportList":[
                  {"ReportMode":1,"WAvgSpeed":0.9}
                ],"ReportProcess":"1"}
                """, planBlockCount: 2));

        var response = await Service(evaluation).GetAsync(
            Context(), Latest(2), "request-1");

        var walking = Assert.Single(response.Data!.Projects);
        Assert.Equal("walking", walking.RehabContent);
        Assert.Equal(2, walking.SameProjectLaneCount);
        Assert.Equal(new[] { 1, 2 },
            walking.Lanes.Select(x => x.SameProjectOrdinal));
        var firstValues = walking.Lanes
            .Select(x => x.MetricSeries.Single(s =>
                    s.MetricCode == "walking_average_speed")
                .Points[0].NormalizedValue)
            .ToArray();
        Assert.Equal(new decimal?[] { 0.5m, 0.7m }, firstValues);
        Assert.Equal(
            "segment_missing",
            walking.Lanes[1].Segments[1].Status);
    }

    [Fact]
    public async Task Missing_middle_segment_keeps_plan_ordinal_when_configuration_identifies_last_segment()
    {
        const string plan = """
            [
              {"ReportMode":1,"Walk_Mode":0},
              {"ReportMode":1,"Walk_Mode":1},
              {"ReportMode":1,"Walk_Mode":2}
            ]
            """;
        var evaluation = Evaluation(
            Session(
                1,
                """
                {"ReportList":[
                  {"ReportMode":1,"WalkMode":0,"WAvgSpeed":0.5},
                  {"ReportMode":1,"WalkMode":2,"WAvgSpeed":0.7}
                ],"ReportProcess":"11"}
                """,
                planBlockCount: 3,
                planJson: plan),
            Session(
                2,
                """
                {"ReportList":[
                  {"ReportMode":1,"WalkMode":0,"WAvgSpeed":0.6},
                  {"ReportMode":1,"WalkMode":1,"WAvgSpeed":0.8},
                  {"ReportMode":1,"WalkMode":2,"WAvgSpeed":0.9}
                ],"ReportProcess":"111"}
                """,
                planBlockCount: 3,
                planJson: plan));

        var response = await Service(evaluation).GetAsync(
            Context(), Latest(2), "request-1");

        var lanes = Assert.Single(response.Data!.Projects).Lanes;
        Assert.Equal(3, lanes.Count);
        Assert.Equal("segment_missing", lanes[1].Segments[0].Status);
        Assert.Equal("present", lanes[2].Segments[0].Status);
        Assert.Equal(
            0.7m,
            lanes[2].MetricSeries.Single(x =>
                x.MetricCode == "walking_average_speed").Points[0]
                .NormalizedValue);
    }

    [Fact]
    public async Task Incomplete_middle_report_keeps_time_position_and_connects_visible_points_solid()
    {
        var evaluation = Evaluation(
            Session(1, Walking(0.5m)),
            Session(2, Walking(0.6m), complete: false),
            Session(3, Walking(0.9m)));

        var response = await Service(evaluation).GetAsync(
            Context(), Latest(3), "request-1");

        Assert.Equal(3, response.Data!.Window.ResolvedReportCount);
        Assert.Equal(2, response.Data.Window.CompleteReportCount);
        Assert.Equal(1, response.Data.Window.IncompleteReportCount);
        Assert.Equal(
            "incomplete",
            response.Data.Reports[1].ReportStatus);
        Assert.Equal(string.Empty, response.Data.Reports[1].SessionRef);
        Assert.NotNull(response.Data.Reports[1].SessionTime);
        Assert.Equal(0, response.Data.Reports[1].ReportBlockCount);
        var series = response.Data.Projects.Single().Lanes.Single()
            .MetricSeries.Single(x => x.MetricCode == "walking_average_speed");
        Assert.Equal("report_incomplete", series.Points[1].Status);
        Assert.Equal("solid", series.Points[2].LineFromPrevious);
        Assert.Equal("same_condition", series.Points[2].ComparisonScope);
        Assert.Contains(
            "missing_report_position_skipped",
            series.Points[2].ComparisonLimitations);
        Assert.Equal(2, series.LongestSameConditionRun);
        Assert.Equal("unavailable", series.Interpretation.Mode);
        Assert.Equal("insufficient", series.Interpretation.Direction);
        Assert.Contains(
            IReGoReasonCodes.LongitudinalIncompleteReportsIncludedAsMissing,
            response.ReasonCodes);
    }

    [Fact]
    public async Task Same_game_at_different_difficulties_stays_in_one_project_with_solid_display_line()
    {
        var evaluation = Evaluation(
            Session(1, Game(difficulty: 1, score: 21), rehabContent: "game"),
            Session(2, Game(difficulty: 3, score: 174), rehabContent: "game"));

        var response = await Service(evaluation).GetAsync(
            Context(), Latest(2), "request-1");

        var project = Assert.Single(response.Data!.Projects);
        Assert.Equal(4, project.GameMode);
        var score = project.Lanes.Single().MetricSeries.Single(x =>
            x.MetricCode == "game_score");
        Assert.Equal(new int?[] { 1, 3 }, score.Points.Select(x => x.GameDifficulty));
        Assert.Equal("solid", score.Points[1].LineFromPrevious);
        Assert.Equal("cross_difficulty", score.Points[1].ComparisonScope);
        Assert.Equal("unavailable", score.Interpretation.Mode);
        Assert.Equal("insufficient", score.Interpretation.Direction);
        Assert.DoesNotContain("表现提升", score.Interpretation.Text);
        Assert.Equal("ineligible", response.Data.Quality.TrendEligibility);
    }

    [Fact]
    public async Task Two_same_condition_reports_are_before_after_not_trend()
    {
        var response = await Service(Evaluation(
                Session(1, Walking(0.5m)),
                Session(2, Walking(0.9m))))
            .GetAsync(Context(), Latest(2), "request-1");

        var series = response.Data!.Projects.Single().Lanes.Single()
            .MetricSeries.Single(x => x.MetricCode == "walking_average_speed");
        Assert.Equal("unavailable", series.Interpretation.Mode);
        Assert.Equal("descriptive_only", response.Data.Window.ComparisonMode);
        Assert.Equal("ineligible", response.Data.Quality.TrendEligibility);
    }

    [Fact]
    public async Task Three_consecutive_same_condition_points_are_trend_eligible()
    {
        var response = await Service(Evaluation(
                Session(1, Walking(0.5m)),
                Session(2, Walking(0.7m)),
                Session(3, Walking(0.9m))))
            .GetAsync(Context(), Latest(3), "request-1");

        var series = response.Data!.Projects.Single().Lanes.Single()
            .MetricSeries.Single(x => x.MetricCode == "walking_average_speed");
        Assert.Equal("trend", series.Interpretation.Mode);
        Assert.Equal("improved", series.Interpretation.Verdict);
        Assert.Equal("步行能力", series.Interpretation.CapabilityName);
        Assert.Equal(
            "您的平均速度连续提高，步行能力改善。",
            series.Interpretation.PatientMessage);
        Assert.Equal(3, series.LongestSameConditionRun);
        Assert.Equal("trend", response.Data.Window.ComparisonMode);
        Assert.Equal(
            "eligible_with_same_project_constraints",
            response.Data.Quality.TrendEligibility);
    }

    [Fact]
    public async Task Unverified_unit_values_are_returned_without_a_comparison_line()
    {
        var response = await Service(Evaluation(
                Session(1, """{"ReportList":[{"ReportMode":1,"WForce":3}]}"""),
                Session(2, """{"ReportList":[{"ReportMode":1,"WForce":4}]}""")))
            .GetAsync(Context(), Latest(2), "request-1");

        var duration = response.Data!.Projects.Single().Lanes.Single()
            .MetricSeries.Single(x => x.MetricCode == "walking_force");
        Assert.False(duration.ComparisonEligible);
        Assert.False(duration.ChartEligible);
        Assert.Contains(
            "unit_not_confirmed_or_mixed",
            duration.ComparisonLimitations);
        Assert.All(duration.Points, point =>
            Assert.Equal("none", point.LineFromPrevious));
    }

    [Fact]
    public async Task Gap_and_game_difficulty_change_are_both_disclosed()
    {
        var response = await Service(Evaluation(
                Session(1, Game(1, 21), rehabContent: "game"),
                Session(2, Game(2, 80), complete: false, rehabContent: "game"),
                Session(3, Game(3, 174), rehabContent: "game")))
            .GetAsync(Context(), Latest(3), "request-1");

        var score = response.Data!.Projects.Single().Lanes.Single()
            .MetricSeries.Single(x => x.MetricCode == "game_score");
        Assert.Equal("cross_difficulty", score.Points[2].ComparisonScope);
        Assert.Contains(
            "missing_report_position_skipped",
            score.Points[2].ComparisonLimitations);
        Assert.Contains("cross_difficulty", score.Points[2].ComparisonLimitations);
        Assert.Equal("insufficient_data", score.Interpretation.Verdict);
        Assert.Contains("只展示记录值", score.Interpretation.Text);
    }

    [Fact]
    public async Task Missing_subitem_date_does_not_split_three_same_condition_points()
    {
        var response = await Service(Evaluation(
                Session(1, Walking(0.5m)),
                Session(2, Walking(0.6m), complete: false),
                Session(3, Walking(0.7m)),
                Session(4, Walking(0.9m))))
            .GetAsync(Context(), Latest(4), "request-1");

        var speed = response.Data!.Projects.Single().Lanes.Single()
            .MetricSeries.Single(x =>
                x.MetricCode == "walking_average_speed");
        Assert.Equal(3, speed.LongestSameConditionRun);
        Assert.Equal("trend", speed.Interpretation.Mode);
        Assert.Equal("improved", speed.Interpretation.Verdict);
        Assert.Equal("solid", speed.Points[2].LineFromPrevious);
        Assert.Equal("same_condition", speed.Points[2].ComparisonScope);
    }

    [Fact]
    public async Task Different_training_projects_are_never_combined()
    {
        var evaluation = Evaluation(
            Session(1, Walking(0.5m)),
            Session(2, """
                {"ReportList":[
                  {"ReportMode":2,"STrainingSitNum":5}
                ],"ReportProcess":"2"}
                """, rehabContent: "sit_stand"));

        var response = await Service(evaluation).GetAsync(
            Context(), Latest(2), "request-1");

        Assert.Equal(2, response.Data!.Projects.Count);
        Assert.Contains(response.Data.Projects, x => x.RehabContent == "walking");
        Assert.Contains(response.Data.Projects, x => x.RehabContent == "sit_stand");
        Assert.All(
            response.Data.Projects.SelectMany(x => x.Lanes),
            lane => Assert.DoesNotContain(
                lane.MetricSeries,
                series => series.Points.Count(point =>
                    point.NormalizedValue.HasValue) > 1));
    }

    [Fact]
    public async Task Report_only_project_is_not_promoted_into_a_comparison_lane()
    {
        var evaluation = Evaluation(
            Session(
                1,
                """
                {"ReportList":[
                  {"ReportMode":1,"WAvgSpeed":0.5},
                  {"ReportMode":2,"STrainingSitNum":5}
                ],"ReportProcess":"12"}
                """,
                planJson: "[{\"ReportMode\":1}]"),
            Session(
                2,
                """
                {"ReportList":[
                  {"ReportMode":1,"WAvgSpeed":0.9},
                  {"ReportMode":2,"STrainingSitNum":7}
                ],"ReportProcess":"12"}
                """,
                planJson: "[{\"ReportMode\":1}]"));

        var response = await Service(evaluation).GetAsync(
            Context(), Latest(2), "request-1");

        var project = Assert.Single(response.Data!.Projects);
        Assert.Equal("walking", project.RehabContent);
    }

    [Fact]
    public async Task Response_and_html_do_not_expose_database_primary_keys()
    {
        var response = await Service(Evaluation(
                Session(1, Walking(0.5m)),
                Session(2, Walking(0.9m))))
            .GetAsync(Context(), Latest(2), "request-1");

        var json = Newtonsoft.Json.JsonConvert.SerializeObject(response.Data);
        var html = new IReGoLongitudinalReportHtmlBuilder().Build(response.Data!);
        foreach (var primaryKey in new[] { "1001", "1002", "2001", "2002" })
        {
            Assert.DoesNotContain(primaryKey, json, StringComparison.Ordinal);
            Assert.DoesNotContain(primaryKey, html, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Vr_game_plan_and_report_use_the_same_project_identity()
    {
        var evaluation = Evaluation(
            Session(
                1,
                """
                {"ReportList":[
                  {"ReportMode":3,"IsVR":true,"GameMode":4,"GDifficulty":1,"GScore":21}
                ],"ReportProcess":"3"}
                """,
                rehabContent: "vr_game"),
            Session(
                2,
                """
                {"ReportList":[
                  {"ReportMode":3,"IsVR":true,"GameMode":4,"GDifficulty":1,"GScore":30}
                ],"ReportProcess":"3"}
                """,
                rehabContent: "vr_game"));

        var response = await Service(evaluation).GetAsync(
            Context(), Latest(2), "request-1");

        var project = Assert.Single(response.Data!.Projects);
        Assert.Equal(4, project.GameMode);
        Assert.Equal("vr_game", project.InteractionType);
        var score = project.Lanes.Single().MetricSeries.Single(x =>
            x.MetricCode == "game_score");
        Assert.Equal(2, score.Points.Count(x => x.Status == "valid"));
        Assert.Equal("solid", score.Points[1].LineFromPrevious);
        Assert.Equal("same_condition", score.Points[1].ComparisonScope);
    }

    [Fact]
    public async Task Endpoint_references_automatically_include_incomplete_middle_position()
    {
        var evaluation = Evaluation(
            Session(1, Walking(0.5m)),
            Session(2, Walking(0.6m), complete: false),
            Session(3, Walking(0.9m)));
        var provider = IReGoSessionRefProviderTests.Provider();
        var first = evaluation.Sessions[0];
        var last = evaluation.Sessions[2];
        var firstRef = Reference(provider, evaluation, first);
        var lastRef = Reference(provider, evaluation, last);

        var response = await Service(evaluation).GetAsync(
            Context(),
            new GetIReGoLongitudinalAnalysisRequestDto
            {
                Selector = "session_refs",
                SessionRefs = new[] { firstRef, lastRef }
            },
            "request-1");

        Assert.Equal(3, response.Data!.Reports.Count);
        Assert.Equal("incomplete", response.Data.Reports[1].ReportStatus);
        Assert.Equal(string.Empty, response.Data.Reports[1].SessionRef);
    }

    [Fact]
    public async Task Recent_ordinals_are_resolved_by_backend_without_session_refs()
    {
        var evaluation = Evaluation(
            Session(1, Walking(0.5m)),
            Session(2, Walking(0.6m)),
            Session(3, Walking(0.7m)),
            Session(4, Walking(0.8m)));

        var response = await Service(evaluation).GetAsync(
            Context(),
            new GetIReGoLongitudinalAnalysisRequestDto
            {
                SelectionMode = "recent_ordinal_range",
                StartOrdinal = 1,
                EndOrdinal = 2
            },
            "request-ordinal");

        Assert.Equal(ToolStatuses.Success, response.Status);
        Assert.Equal(2, response.Data!.Reports.Count);
        Assert.Equal(13, response.Data.Window.StartedAt!.Value.Day);
        Assert.Equal(14, response.Data.Window.EndedAt!.Value.Day);
    }

    [Fact]
    public async Task Date_range_is_resolved_from_trusted_report_times()
    {
        var evaluation = Evaluation(
            Session(1, Walking(0.5m)),
            Session(2, Walking(0.6m)),
            Session(3, Walking(0.7m)),
            Session(4, Walking(0.8m)));

        var response = await Service(evaluation).GetAsync(
            Context(),
            new GetIReGoLongitudinalAnalysisRequestDto
            {
                SelectionMode = "date_range",
                StartDate = new DateTimeOffset(
                    2026, 5, 12, 0, 0, 0, TimeSpan.FromHours(8)),
                EndDate = new DateTimeOffset(
                    2026, 5, 13, 23, 59, 59, TimeSpan.FromHours(8))
            },
            "request-dates");

        Assert.Equal(ToolStatuses.Success, response.Status);
        Assert.Equal(2, response.Data!.Reports.Count);
        Assert.Equal(12, response.Data.Window.StartedAt!.Value.Day);
        Assert.Equal(13, response.Data.Window.EndedAt!.Value.Day);
    }

    [Fact]
    public async Task Latest_count_of_ten_is_allowed()
    {
        var evaluation = Evaluation(Enumerable.Range(1, 10)
            .Select(index => Session(index, Walking(index / 10m)))
            .ToArray());

        var response = await Service(evaluation).GetAsync(
            Context(), Latest(10), "request-max-ten");

        Assert.NotEqual(ToolStatuses.Failed, response.Status);
        Assert.Equal(10, response.Data!.Window.RequestedReportCount);
        Assert.Equal(10, response.Data.Window.ResolvedReportCount);
    }

    [Fact]
    public async Task Latest_count_above_ten_is_rejected_without_truncation()
    {
        var evaluation = Evaluation(Enumerable.Range(1, 11)
            .Select(index => Session(index, Walking(index / 10m)))
            .ToArray());

        var response = await Service(evaluation).GetAsync(
            Context(), Latest(11), "request-over-ten");

        Assert.Equal(ToolStatuses.Failed, response.Status);
        Assert.Null(response.Data);
        Assert.Contains(
            IReGoReasonCodes.LongitudinalWindowTooLarge,
            response.ReasonCodes);
        Assert.Contains("2至10", response.PatientMessage);
    }

    [Fact]
    public async Task Reference_from_another_patient_cannot_define_window()
    {
        var evaluation = Evaluation(
            Session(1, Walking(0.5m)),
            Session(2, Walking(0.9m)));
        var provider = IReGoSessionRefProviderTests.Provider();
        var first = Reference(provider, evaluation, evaluation.Sessions[0]);
        var other = provider.Create(
            3,
            999,
            evaluation.Sessions[1].Plan.Id,
            evaluation.Sessions[1].Report.Id,
            evaluation.Sessions[1].ParsedReport.SchemaSignature);

        var response = await Service(evaluation).GetAsync(
            Context(),
            new GetIReGoLongitudinalAnalysisRequestDto
            {
                Selector = "session_refs",
                SessionRefs = new[] { first, other }
            },
            "request-1");

        Assert.Equal(ToolStatuses.Failed, response.Status);
        Assert.Contains(IReGoReasonCodes.SessionRefNotFound, response.ReasonCodes);
    }

    [Fact]
    public async Task Html_report_omits_projects_with_fewer_than_three_records()
    {
        var response = await Service(Evaluation(
                Session(1, Game(1, 21), rehabContent: "game"),
                Session(2, Game(2, 80), complete: false, rehabContent: "game"),
                Session(3, Game(3, 174), rehabContent: "game")))
            .GetAsync(Context(), Latest(3), "request-1");

        var html = new IReGoLongitudinalReportHtmlBuilder().Build(response.Data!);

        Assert.Contains("本报告按日期汇总训练情况", html);
        Assert.DoesNotContain("数据不足", html);
        Assert.DoesNotContain("project-insufficient", html);
        Assert.DoesNotContain("同项目覆盖矩阵", html);
        Assert.DoesNotContain("stroke-dasharray", html);
        Assert.DoesNotContain("AI评价", html);
        Assert.Contains("统合汇报", html);
        Assert.DoesNotContain("report_id", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Cross_difficulty_game_keeps_api_layers_but_report_omits_extended_explanation()
    {
        var response = await Service(Evaluation(
                Session(
                    1,
                    GameWithDuration(1, 100, 100),
                    rehabContent: "game"),
                Session(
                    2,
                    GameWithDuration(1, 110, 100),
                    rehabContent: "game"),
                Session(
                    3,
                    GameWithDuration(2, 100, 100),
                    rehabContent: "game")))
            .GetAsync(Context(), Latest(3), "request-game-cross");

        var evaluation = Assert.Single(response.Data!.Projects.Single()
            .CapabilityEvaluations.Where(x =>
                x.CapabilityCode == "game_training_performance"));
        Assert.Equal("improved", evaluation.Verdict);
        Assert.Contains("难度提高后", evaluation.BasicEvaluation);
        Assert.Contains("90%", evaluation.ExtendedExplanation);
        Assert.Contains("持续注意、及时反应和手脚配合完成任务", evaluation.LifeHint);
        Assert.DoesNotContain("不能单独说明", evaluation.LifeHint);
        Assert.DoesNotContain("康复评估", evaluation.LifeHint);
        Assert.Equal(evaluation.BasicEvaluation, evaluation.PatientMessage);

        var html = new IReGoLongitudinalReportHtmlBuilder().Build(response.Data);
        Assert.DoesNotContain("基础评价", html);
        Assert.DoesNotContain("扩展解释", html);
        Assert.DoesNotContain(evaluation.ExtendedExplanation, html);
        Assert.DoesNotContain("生活提示", html);
        Assert.DoesNotContain(evaluation.LifeHint, html);
        Assert.Contains("持续注意、及时反应和手脚配合完成任务可能会更顺畅。", html);
        Assert.DoesNotContain("是否转换为日常能力还需康复评估", html);
    }

    private static IReGoLongitudinalAnalysisService Service(
        IReGoPatientEvaluation evaluation) => new(
            new FakeEvaluationService(evaluation),
            IReGoSessionRefProviderTests.Provider(),
            new IReGoEvaluationRuleEngine(
                new IReGoThresholdRegistry(),
                new IReGoStrokeCapabilityMapper(),
                new IReGoPatientMessageComposer()),
            Options.Create(new IReGoToolOptions
            {
                SessionRefSigningKey =
                    "unit-test-only-persistent-signing-key-32-bytes",
                SourceCompleteness = "authoritative_primary"
            }));

    private static GetIReGoLongitudinalAnalysisRequestDto Latest(int count) =>
        new()
        {
            Selector = "latest_contiguous",
            ReportCount = count,
            DetailLevel = "standard",
            QualityDetail = "summary"
        };

    private static TrustedPatientContext Context() => new()
    {
        PatientId = 461,
        SubjectRef = "dify_user:461"
    };

    private static string Reference(
        IIReGoSessionRefProvider provider,
        IReGoPatientEvaluation evaluation,
        IReGoEvaluatedSession session) => provider.Create(
            evaluation.Raw.Patient!.BusinessId,
            461,
            session.Plan.Id,
            session.Report.Id,
            session.ParsedReport.SchemaSignature);

    private static IReGoPatientEvaluation Evaluation(
        params IReGoEvaluatedSession[] sessions) => new()
        {
            Raw = new IReGoRawDataSet
            {
                Patient = new PatientEntity
                {
                    Id = 461,
                    BusinessId = 3,
                    IsDeleted = false
                },
                SourceSystem = "project",
                SourceCompleteness = "authoritative_primary"
            },
            Sessions = sessions,
            StrictPlanCount = sessions.Length,
            StrictReportCount = sessions.Length,
            DataState = "supported_session_available",
            Watermark = "longitudinal-test-watermark"
        };

    private static IReGoEvaluatedSession Session(
        int ordinal,
        string reportJson,
        bool complete = true,
        int planBlockCount = 1,
        string rehabContent = "walking",
        string? planJson = null)
    {
        var time = new DateTime(2026, 5, 10 + ordinal, 9, 0, 0);
        planJson ??= Plan(rehabContent, planBlockCount);
        return new IReGoEvaluatedSession
        {
            Plan = new RehabPlanEntity
            {
                Id = 1000 + ordinal,
                UserId = 461,
                BusinessId = 3,
                DeviceId = 2,
                TemplateId = 4,
                CreateTime = time,
                BookingTime = time,
                EndTime = time.AddMinutes(20),
                IsComplete = complete ? 1 : 0,
                Details = planJson
            },
            Report = new RehabReportEntity
            {
                Id = 2000 + ordinal,
                PlanId = 1000 + ordinal,
                ReportDetails = reportJson,
                CreateTime = time.AddMinutes(20)
            },
            Device = new DeviceEntity { Id = 2, DeviceType = 1 },
            Template = new RehabTemplateEntity
            {
                Id = 4,
                ModelId = 1,
                Details = planJson
            },
            ParsedReport = new IReGoReportParser().Parse(reportJson),
            State = new TrainingStateResult
            {
                State = complete ? "completed" : "interrupted",
                Confidence = "high"
            },
            Usable = complete,
            EligibleForLatest = true,
            ValidationStatus = complete
                ? "structurally_valid"
                : "not_completed"
        };
    }

    private static string Plan(string content, int count)
    {
        var item = content switch
        {
            "sit_stand" => "{\"ReportMode\":2}",
            "game" => "{\"ReportMode\":3,\"Gaming_Mode\":4}",
            "vr_game" => "{\"ReportMode\":3,\"IsVR\":true,\"Gaming_Mode\":4}",
            _ => "{\"ReportMode\":1}"
        };
        return "[" + string.Join(",", Enumerable.Repeat(item, count)) + "]";
    }

    private static string Walking(decimal speed) =>
        $"{{\"ReportList\":[{{\"ReportMode\":1,\"WAvgSpeed\":{speed.ToString(System.Globalization.CultureInfo.InvariantCulture)}}}],\"ReportProcess\":\"1\"}}";

    private static string Game(int difficulty, int score) =>
        $"{{\"ReportList\":[{{\"ReportMode\":3,\"GameMode\":4,\"GDifficulty\":{difficulty},\"GScore\":{score}}}],\"ReportProcess\":\"3\"}}";

    private static string GameWithDuration(
        int difficulty,
        int score,
        int duration) =>
        $"{{\"ReportList\":[{{\"ReportMode\":3,\"GameMode\":4,\"GDifficulty\":{difficulty},\"GScore\":{score},\"GTrainingTime\":{duration}}}],\"ReportProcess\":\"3\"}}";

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
