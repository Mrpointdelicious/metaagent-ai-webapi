using AI_WebApi.Model.DTOs.AITools;
using AI_WebApi.Services.AITools.IReGo;

namespace AI_WebApi.Tests;

public sealed class IReGoLongitudinalReportHtmlBuilderTests
{
    [Fact]
    public void Overview_uses_patient_friendly_rule_grounded_evaluation()
    {
        var walking = Evaluation(
            "walking_ability",
            "步行能力",
            "improved",
            "相同条件下步行速度连续提高，并超过当前评价下限。");
        var sitStand = Evaluation(
            "sit_stand_ability",
            "坐站能力",
            "improved",
            "动作完成更快，站立保持时间增加，两项方向一致。");
        var game = Evaluation(
            "game_training_performance",
            "游戏任务表现",
            "improved",
            "同难度下单位时间得分提高。");
        var analysis = Analysis(
            new[] { walking, sitStand, game },
            new[]
            {
                Project(
                    "walking",
                    "步行训练",
                    longestRun: 3,
                    new[] { walking }),
                Project(
                    "balance_training",
                    "平衡训练",
                    longestRun: 2,
                    Array.Empty<IReGoCapabilityEvaluationDto>())
            });

        var html = new IReGoLongitudinalReportHtmlBuilder().Build(analysis);

        Assert.Contains("这段时间训练表现有进步", html);
        Assert.Contains("走得更快", html);
        Assert.Contains("起身和站立更顺", html);
        Assert.Contains("完成游戏任务更有效率", html);
        Assert.DoesNotContain("平衡训练", html);
        Assert.Contains("日常走路、短距离移动和转弯可能会更顺畅。", html);
        Assert.Contains("从椅子上起身和床椅转移可能会更顺畅。", html);
        Assert.Contains("持续注意、及时反应和手脚配合完成任务可能会更顺畅。", html);
        Assert.Contains("AI 基于规则生成", html);
        Assert.Equal(1, CountOccurrences(html, "<h3>智能评价</h3>"));
        Assert.DoesNotContain("基础评价 ·", html);
        Assert.DoesNotContain("生活提示</strong>", html);
        Assert.DoesNotContain("这项训练表现与相关日常活动有关。", html);
        Assert.DoesNotContain("是否转换为日常能力还需康复评估", html);
        Assert.DoesNotContain("同项目覆盖矩阵", html);
        Assert.DoesNotContain("SAME PROJECT COVERAGE", html);
        Assert.DoesNotContain("不应出现在报表中的扩展解释", html);
        Assert.DoesNotContain("扩展解释", html);
        Assert.Contains(
            "本报告按日期汇总训练情况。同一训练项目有三个及以上有效记录方可生成解析；可见记录按时间顺序连接。AI依据训练表现生成评价，不等同于病情改善。",
            html);
        Assert.Contains(
            "AI 依据训练表现生成评价，不等同于病情改善。",
            html);
        Assert.DoesNotContain("AI 仅依据版本化规则", html);
    }

    [Fact]
    public void Projects_with_insufficient_records_are_not_rendered()
    {
        var analysis = Analysis(
            Array.Empty<IReGoCapabilityEvaluationDto>(),
            new[]
            {
                Project(
                    "balance_training",
                    "平衡训练",
                    longestRun: 2,
                    Array.Empty<IReGoCapabilityEvaluationDto>())
            });

        var html = new IReGoLongitudinalReportHtmlBuilder().Build(analysis);

        Assert.Contains("训练记录还不够，暂时无法判断变化", html);
        Assert.DoesNotContain("平衡训练", html);
        Assert.DoesNotContain("project-insufficient", html);
        Assert.DoesNotContain("数据不足", html);
        Assert.DoesNotContain("SAME PROJECT", html);
        Assert.DoesNotContain("测试指标", html);
        Assert.DoesNotContain("这段时间训练表现有进步", html);
    }

    [Fact]
    public void Game_difficulty_and_mode_are_only_annotated_on_score_chart()
    {
        var project = ProjectWithSeries(
            "game",
            "认知游戏",
            "vr_game",
            new[]
            {
                Series("game_score", "游戏得分", "score", 80m, 95m, 110m,
                    new int?[] { 1, 2, 2 }),
                Series("game_difficulty", "游戏难度", null, 1m, 2m, 2m),
                Series("game_mode", "游戏模式", null, 4m, 4m, 4m)
            },
            gameMode: 4);
        var html = new IReGoLongitudinalReportHtmlBuilder().Build(
            Analysis(Array.Empty<IReGoCapabilityEvaluationDto>(), new[] { project }));

        Assert.Contains("游戏得分趋势", html);
        Assert.Contains("难度1 &#183; 模式4", html);
        Assert.Contains("难度2 &#183; 模式4", html);
        Assert.DoesNotContain("<h4>游戏难度", html);
        Assert.DoesNotContain("<h4>游戏模式", html);
        Assert.DoesNotContain("<table", html);
        Assert.DoesNotContain("<div class=\"value-grid\">", html);
        Assert.Equal(1, CountOccurrences(html, "<section class=\"metric-card\">"));
    }

    [Fact]
    public void Walking_parameters_use_lines_and_share_axis_when_units_match()
    {
        var project = ProjectWithSeries(
            "walking",
            "步行训练",
            "direct_training",
            new[]
            {
                Series("walking_average_speed", "平均步行速度", "m/s", 0.4m, 0.5m, 0.6m),
                Series("walking_max_speed", "最大步行速度", "m/s", 0.7m, 0.8m, 0.9m),
                Series("walking_plan_speed", "计划步行速度", "m/s", 0.5m, 0.6m, 0.7m),
                Series("walking_step_length", "平均步长", "m", 0.3m, 0.35m, 0.4m)
            });
        var html = new IReGoLongitudinalReportHtmlBuilder().Build(
            Analysis(Array.Empty<IReGoCapabilityEvaluationDto>(), new[] { project }));

        Assert.Contains("<h4>平均步行速度 &#183; 最大步行速度 &#183; 计划步行速度</h4>", html);
        Assert.Contains("<span>单位：m/s</span>", html);
        Assert.DoesNotContain("共享纵轴", html);
        Assert.DoesNotContain("参数趋势", html);
        Assert.Contains("平均步行速度、最大步行速度、计划步行速度跨报告折线图", html);
        Assert.Contains("平均步长跨报告折线图", html);
        Assert.Contains("trend-chart-row has-legend", html);
        Assert.Contains("</svg></div><div class=\"trend-legend\">", html);
        Assert.DoesNotContain("<table", html);
        Assert.Equal(2, CountOccurrences(html, "<section class=\"metric-card\">"));
    }

    private static IReGoLongitudinalAnalysisDto Analysis(
        IReadOnlyList<IReGoCapabilityEvaluationDto> evaluations,
        IReadOnlyList<IReGoLongitudinalProjectDto> projects) => new()
        {
            Window = new IReGoLongitudinalWindowDto
            {
                ResolvedReportCount = 3,
                CompleteReportCount = 3,
                StartedAt = DateTimeOffset.Parse("2026-01-01T09:00:00+08:00"),
                EndedAt = DateTimeOffset.Parse("2026-01-03T09:00:00+08:00")
            },
            Reports = Enumerable.Range(1, 3)
                .Select(index => new IReGoLongitudinalReportPositionDto
                {
                    Ordinal = index,
                    SessionTime = DateTimeOffset.Parse(
                        $"2026-01-0{index}T09:00:00+08:00"),
                    IncludedInMetricComparison = true,
                    ReportBlockCount = 1,
                    CompletionRate = 100,
                    CompletionNumerator = 1,
                    CompletionDenominator = 1
                })
                .ToArray(),
            Projects = projects,
            CapabilityEvaluations = evaluations
        };

    private static IReGoLongitudinalProjectDto Project(
        string rehabContent,
        string displayName,
        int longestRun,
        IReadOnlyList<IReGoCapabilityEvaluationDto> evaluations) => new()
        {
            ProjectKey = rehabContent,
            DisplayName = displayName,
            RehabContent = rehabContent,
            InteractionType = "direct_training",
            SameProjectLaneCount = 1,
            Lanes = new[]
            {
                new IReGoLongitudinalLaneDto
                {
                    SameProjectOrdinal = 1,
                    DisplayName = displayName,
                    Segments = Enumerable.Range(1, 3)
                        .Select(index => new IReGoLongitudinalSegmentDto
                        {
                            ReportOrdinal = index,
                            Status = "present"
                        })
                        .ToArray(),
                    MetricSeries = new[]
                    {
                        new IReGoLongitudinalMetricSeriesDto
                        {
                            MetricCode = rehabContent + "_metric",
                            DisplayName = "测试指标",
                            LongestSameConditionRun = longestRun,
                            ChartEligible = false,
                            Points = Enumerable.Range(1, longestRun)
                                .Select(index =>
                                    new IReGoLongitudinalMetricPointDto
                                    {
                                        ReportOrdinal = index,
                                        Status = "valid",
                                        NormalizedValue = index,
                                        DisplayValue = index.ToString()
                                    })
                                .ToArray()
                        }
                    }
                }
            },
            CapabilityEvaluations = evaluations
        };

    private static IReGoLongitudinalProjectDto ProjectWithSeries(
        string rehabContent,
        string displayName,
        string interactionType,
        IReadOnlyList<IReGoLongitudinalMetricSeriesDto> series,
        int? gameMode = null) => new()
        {
            ProjectKey = rehabContent,
            DisplayName = displayName,
            RehabContent = rehabContent,
            InteractionType = interactionType,
            GameMode = gameMode,
            SameProjectLaneCount = 1,
            Lanes = new[]
            {
                new IReGoLongitudinalLaneDto
                {
                    SameProjectOrdinal = 1,
                    DisplayName = displayName,
                    Segments = Enumerable.Range(1, 3)
                        .Select(index => new IReGoLongitudinalSegmentDto
                        {
                            ReportOrdinal = index,
                            Status = "present"
                        })
                        .ToArray(),
                    MetricSeries = series
                }
            }
        };

    private static IReGoLongitudinalMetricSeriesDto Series(
        string metricCode,
        string displayName,
        string? unit,
        decimal first,
        decimal second,
        decimal third,
        IReadOnlyList<int?>? difficulties = null)
    {
        var values = new[] { first, second, third };
        return new IReGoLongitudinalMetricSeriesDto
        {
            MetricCode = metricCode,
            DisplayName = displayName,
            Unit = unit,
            LongestSameConditionRun = 3,
            Points = values.Select((value, index) =>
                new IReGoLongitudinalMetricPointDto
                {
                    ReportOrdinal = index + 1,
                    Status = "valid",
                    NormalizedValue = value,
                    DisplayValue = value.ToString(),
                    GameDifficulty = difficulties?[index]
                }).ToArray()
        };
    }

    private static int CountOccurrences(string value, string token) =>
        value.Split(token, StringSplitOptions.None).Length - 1;

    private static IReGoCapabilityEvaluationDto Evaluation(
        string code,
        string name,
        string verdict,
        string explanation) => new()
        {
            CapabilityCode = code,
            CapabilityName = name,
            Verdict = verdict,
            BasicEvaluation = $"您的{name}改善。",
            ExtendedExplanation =
                "不应出现在报表中的扩展解释。" + explanation,
            LifeHint = "这项训练表现与相关日常活动有关。"
        };
}
