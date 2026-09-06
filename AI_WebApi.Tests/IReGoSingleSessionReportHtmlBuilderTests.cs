using AI_WebApi.Model.DTOs.AITools;
using AI_WebApi.Services.AITools.IReGo;

namespace AI_WebApi.Tests;

public sealed class IReGoSingleSessionReportHtmlBuilderTests
{
    [Fact]
    public void Html_matches_confirmed_patient_facing_template_rules()
    {
        var plan = Block(1, "game", "game", "宇宙拼图",
            Metric("plan_game_mode", 3),
            Metric("plan_set_time", 1));
        var report = Block(1, "game", "game", "宇宙拼图",
            Metric("game_mode", 3),
            Metric("game_difficulty", 2),
            Metric("game_score", 10),
            Metric("game_training_duration", 1));
        var analysis = new IReGoSessionAnalysisDto
        {
            Time = new IReGoSessionTimeDto
            {
                ScheduledAt = DateTimeOffset.Parse("2025-05-21T09:00:00+08:00"),
                ReportRecordedAt = DateTimeOffset.Parse("2025-05-21T14:10:00+08:00"),
                TrainingState = "completed"
            },
            Plan = new IReGoPlanDetailDto { Blocks = new[] { plan } },
            ReportBlocks = new[] { report },
            BlockComparison = new[]
            {
                new IReGoBlockComparisonDto
                {
                    PlanSequence = 1,
                    ReportSequence = 1,
                    Status = "matched_exact"
                }
            }
        };

        var html = new IReGoSingleSessionReportHtmlBuilder().Build(
            analysis,
            Evaluation(1, "excellent"));

        Assert.Contains("完成率", html);
        Assert.Contains("100%", html);
        Assert.Contains("AI评价", html);
        Assert.Contains("本次完成率100%", html);
        Assert.Contains("本次游戏表现评价为<b>未评价</b>", html);
        Assert.Contains("宇宙拼图", html);
        Assert.Contains("训练得分", html);
        Assert.Contains(">10<", html);
        var heroEnd = html.IndexOf("</header>", StringComparison.Ordinal);
        var heroHtml = html[..heroEnd];
        Assert.DoesNotContain("训练游戏得分", heroHtml);
        Assert.Equal(4, CountOccurrences(heroHtml, "<span>"));
        Assert.Contains("计划与实际训练对照", html);
        var comparisonStart = html.IndexOf(
            "计划与实际训练对照",
            StringComparison.Ordinal);
        var detailsStart = html.IndexOf(
            "各训练项目详情",
            StringComparison.Ordinal);
        var comparisonHtml = html[comparisonStart..detailsStart];
        Assert.Contains("游戏模式", comparisonHtml);
        Assert.DoesNotContain("训练难度", comparisonHtml);
        Assert.DoesNotContain("训练得分", comparisonHtml);
        Assert.DoesNotContain("<span>训练时长</span>", comparisonHtml);
        Assert.DoesNotContain("暂不可比", html);
        Assert.DoesNotContain("可比项目完成率", html);
        Assert.DoesNotContain("样本计数", html);
        Assert.DoesNotContain("ReportMode", html);
        Assert.DoesNotContain("数据质量", html);
    }

    [Fact]
    public void Comparison_displays_actual_parameter_when_plan_contains_counterpart()
    {
        var plan = Block(1, "game", "game", "宇宙拼图",
            Metric("plan_game_mode", 3),
            Metric("plan_game_intensity", 1));
        var report = Block(1, "game", "game", "宇宙拼图",
            Metric("game_mode", 3),
            Metric("game_difficulty", 2));
        var analysis = new IReGoSessionAnalysisDto
        {
            Plan = new IReGoPlanDetailDto { Blocks = new[] { plan } },
            ReportBlocks = new[] { report },
            BlockComparison = new[]
            {
                new IReGoBlockComparisonDto
                {
                    PlanSequence = 1,
                    ReportSequence = 1,
                    Status = "matched_exact"
                }
            }
        };

        var html = new IReGoSingleSessionReportHtmlBuilder().Build(
            analysis,
            CompletionOnlyEvaluation());
        var comparisonStart = html.IndexOf(
            "计划与实际训练对照",
            StringComparison.Ordinal);
        var detailsStart = html.IndexOf(
            "各训练项目详情",
            StringComparison.Ordinal);
        var comparisonHtml = html[comparisonStart..detailsStart];

        Assert.Contains("训练难度", comparisonHtml);
        Assert.Contains(">2<", comparisonHtml);
    }

    private static int CountOccurrences(string value, string search)
    {
        var count = 0;
        var startIndex = 0;
        while ((startIndex = value.IndexOf(
                   search,
                   startIndex,
                   StringComparison.Ordinal)) >= 0)
        {
            count++;
            startIndex += search.Length;
        }

        return count;
    }

    private static IReGoContentBlockDto Block(
        int sequence,
        string content,
        string interaction,
        string name,
        params MetricFactDto[] metrics) => new()
        {
            Sequence = sequence,
            RehabContent = content,
            InteractionType = interaction,
            Name = name,
            DisplayName = name,
            Metrics = metrics
        };

    private static MetricFactDto Metric(string code, object value) => new()
    {
        MetricCode = code,
        RawValue = value,
        NormalizedValue = Convert.ToDecimal(value),
        ValueStatus = "valid",
        UnitStatus = "unverified"
    };

    private static IReGoSingleSessionAiEvaluationDto Evaluation(
        int gameSequence,
        string gameLevel) => new()
        {
            CompletionLevel = "high",
            GameEvaluations = new[]
            {
                new IReGoGamePerformanceEvaluationDto
                {
                    ReportSequence = gameSequence,
                    PerformanceLevel = gameLevel
                }
            }
        };

    private static IReGoSingleSessionAiEvaluationDto CompletionOnlyEvaluation() =>
        new()
        {
            CompletionLevel = "high"
        };
}
