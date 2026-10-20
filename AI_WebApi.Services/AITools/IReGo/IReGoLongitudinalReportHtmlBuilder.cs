using System.Globalization;
using System.Net;
using System.Text;
using AI_WebApi.IServices.AITools;
using AI_WebApi.Model.DTOs.AITools;

namespace AI_WebApi.Services.AITools.IReGo;

public sealed class IReGoLongitudinalReportHtmlBuilder :
    IIReGoLongitudinalReportHtmlBuilder
{
    private const string EvaluationBoundary =
        "AI 依据训练表现生成评价，不等同于病情改善。";
    private const string ReportUsage =
        "本报告按日期汇总训练情况。同一训练项目有三个及以上有效记录方可生成解析；可见记录按时间顺序连接。AI依据训练表现生成评价，不等同于病情改善。";

    public string Build(IReGoLongitudinalAnalysisDto analysis)
    {
        var html = new StringBuilder();
        html.Append("<!doctype html><html lang=\"zh-CN\"><head>")
            .Append("<meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">")
            .Append("<title>IREGO 统合汇报</title><style>")
            .Append(Styles)
            .Append("</style></head><body><article class=\"report\">");
        AppendHeader(html, analysis);
        html.Append("<main>");
        AppendTimeline(html, analysis);
        AppendProjects(html, analysis);
        html.Append("</main><section class=\"notice\"><strong>报告使用说明</strong>")
            .Append(ReportUsage)
            .Append("</section><footer><span>IREGO 统合汇报</span><span>数据来源：robotDB 训练报告记录</span></footer>")
            .Append("</article></body></html>");
        return html.ToString();
    }

    private static void AppendHeader(
        StringBuilder html,
        IReGoLongitudinalAnalysisDto analysis)
    {
        var start = FormatDate(analysis.Window.StartedAt);
        var end = FormatDate(analysis.Window.EndedAt);
        var range = start == end ? start : $"{start}—{end}";
        html.Append("<header class=\"hero\"><div class=\"eyebrow\">IREGO · INTEGRATED REPORT</div>")
            .Append("<h1>统合汇报</h1><p>所选报告范围 · 只进行同项目、同分段评价</p>")
            .Append("<div class=\"facts\">")
            .Append(Fact("报告范围", range))
            .Append(Fact("报告位置", $"{analysis.Window.ResolvedReportCount} 份"))
            .Append(Fact("完整报告", $"{analysis.Window.CompleteReportCount} 份"))
            .Append(Fact("不完整报告", $"{analysis.Window.IncompleteReportCount} 份"))
            .Append("</div></header>");
    }

    private static void AppendTimeline(
        StringBuilder html,
        IReGoLongitudinalAnalysisDto analysis)
    {
        html.Append(SectionHead(
                "01 · OVERVIEW",
                "总览",
                "按日期展示训练报告完整与不完整情况。"))
            .Append("<div class=\"timeline\">");
        foreach (var report in analysis.Reports)
        {
            var stateClass = report.IncludedInMetricComparison
                ? "complete"
                : "incomplete";
            var stateLabel = report.IncludedInMetricComparison
                ? "完整"
                : "不完整";
            var completion = report.CompletionRate.HasValue
                ? $"完成率 {report.CompletionRate:0.#}% · {report.CompletionNumerator}/{report.CompletionDenominator}"
                : "不参与指标比较";
            var timeLabel = FormatTime(report.SessionTime);
            var reportLabel = report.IncludedInMetricComparison
                ? $"{stateLabel} · {report.ReportBlockCount}段"
                : stateLabel;
            html.Append("<div class=\"visit ").Append(stateClass).Append("\">")
                .Append("<span class=\"visit-number\">").Append(report.Ordinal).Append("</span>")
                .Append("<div><b>").Append(E(timeLabel)).Append("</b><p>")
                .Append(E(completion)).Append("</p></div><em>")
                .Append(E(reportLabel)).Append("</em></div>");
        }
        html.Append("</div>");
        AppendOverallEvaluation(html, analysis);
        html.Append("</section>");
    }

    private static void AppendOverallEvaluation(
        StringBuilder html,
        IReGoLongitudinalAnalysisDto analysis)
    {
        var evaluations = analysis.CapabilityEvaluations
            .Where(x => IsEvaluationVerdict(x.Verdict))
            .ToArray();
        var title = OverallTitle(evaluations);
        var performanceSummary = BuildPerformanceSummary(evaluations);
        var lifeSummary = BuildLifeSummary(evaluations);

        html.Append("<div class=\"smart-eval overview-eval\"><div class=\"evaluation-heading\">")
            .Append("<h3>智能评价</h3><span class=\"ai-rule-badge\">AI 基于规则生成</span>")
            .Append("</div><div class=\"overview-summary\"><span class=\"status-badge ")
            .Append(E(OverallStatusClass(evaluations))).Append("\">")
            .Append(E(title)).Append("</span><div><strong>")
            .Append(E(performanceSummary)).Append("</strong><p>")
            .Append(E(lifeSummary)).Append("</p></div></div>");

        if (evaluations.Length > 0)
        {
            html.Append("<div class=\"overview-eval-grid\">");
            foreach (var evaluation in evaluations)
            {
                var status = StatusLabel(evaluation.Verdict);
                html.Append("<div class=\"overview-eval-item ")
                    .Append(E(VerdictClass(evaluation.Verdict))).Append("\"><b>")
                    .Append(E(evaluation.CapabilityName)).Append(" · ")
                    .Append(E(status)).Append("</b><p>")
                    .Append(E(OverviewLifeHint(evaluation)))
                    .Append("</p></div>");
            }
            html.Append("</div>");
        }

        html.Append("<div class=\"evaluation-boundary\">")
            .Append(EvaluationBoundary)
            .Append("</div></div>");
    }

    private static void AppendProjects(
        StringBuilder html,
        IReGoLongitudinalAnalysisDto analysis)
    {
        var section = 2;
        foreach (var project in analysis.Projects)
        {
            var eligibleLanes = project.Lanes
                .Select(lane => new
                {
                    Lane = lane,
                    Series = lane.MetricSeries
                        .Where(series => IsRenderableSeries(project, series))
                        .ToArray()
                })
                .Where(x => x.Series.Length > 0)
                .ToArray();
            if (eligibleLanes.Length == 0)
            {
                continue;
            }
            var gameLabel = project.InteractionType is "game" or "vr_game"
                ? "同游戏 · 得分按报告比较"
                : "同训练项目";
            html.Append(SectionHead(
                $"{section:00} · SAME PROJECT",
                project.DisplayName,
                gameLabel));
            foreach (var eligibleLane in eligibleLanes)
            {
                var lane = eligibleLane.Lane;
                html.Append("<article class=\"module\"><div class=\"module-head ")
                    .Append(ProjectClass(project)).Append("\"><div><small>同项目分段 ")
                    .Append(lane.SameProjectOrdinal).Append("</small><h3>")
                    .Append(E(lane.DisplayName)).Append("</h3></div><span>")
                    .Append(E(SegmentSummary(lane))).Append("</span></div>")
                    .Append("<div class=\"metric-list\">");
                foreach (var group in BuildChartGroups(eligibleLane.Series))
                {
                    AppendMetricGroup(
                        html,
                        group,
                        analysis.Reports,
                        project,
                        lane.MetricSeries);
                }
                html.Append("</div></article>");
            }
            html.Append("</section>");
            section++;
        }
    }

    private static void AppendMetricGroup(
        StringBuilder html,
        ChartGroup group,
        IReadOnlyList<IReGoLongitudinalReportPositionDto> reports,
        IReGoLongitudinalProjectDto project,
        IReadOnlyList<IReGoLongitudinalMetricSeriesDto> contextSeries)
    {
        var unit = group.Unit;
        var gameScore = group.Series.Any(x => x.MetricCode == "game_score");
        var title = gameScore
            ? "游戏得分趋势"
            : group.Series.Count == 1
                ? group.Series[0].DisplayName
                : string.Join(" · ", group.Series.Select(x => x.DisplayName));
        html.Append("<section class=\"metric-card\"><div class=\"metric-title\"><h4>")
            .Append(E(title)).Append("</h4><span>");
        if (gameScore)
        {
            html.Append("难度与游戏模式标注在日期下方");
        }
        else if (group.Series.Count > 1)
        {
            if (!string.IsNullOrWhiteSpace(unit))
            {
                html.Append("单位：").Append(E(unit));
            }
        }
        else if (!string.IsNullOrWhiteSpace(unit))
        {
            html.Append("单位：").Append(E(unit));
        }
        else
        {
            html.Append("折线趋势");
        }
        html.Append("</span></div>")
            .Append(BuildChart(
                group,
                reports,
                project,
                contextSeries))
            .Append("</section>");
    }

    // Other device reports share the exact chart markup, geometry and colors.
    // Device adapters provide display-ready values without invoking IReGo evaluation rules.
    internal static void AppendMetricChart(
        StringBuilder html,
        IReGoLongitudinalMetricSeriesDto series,
        IReadOnlyList<IReGoLongitudinalReportPositionDto> reports) =>
        AppendMetricGroup(
            html,
            new ChartGroup(new[] { series }, series.Unit),
            reports,
            new IReGoLongitudinalProjectDto(),
            Array.Empty<IReGoLongitudinalMetricSeriesDto>());

    private static bool IsEvaluationVerdict(string verdict) => verdict is
        "improved" or "stable" or "worsened" or "mixed";

    private static string OverallTitle(
        IReadOnlyCollection<IReGoCapabilityEvaluationDto> evaluations)
    {
        if (evaluations.Count == 0)
        {
            return "训练记录还不够，暂时无法判断变化";
        }

        var verdicts = evaluations.Select(x => x.Verdict)
            .ToHashSet(StringComparer.Ordinal);
        if (verdicts.Contains("mixed") ||
            verdicts.Contains("improved") && verdicts.Contains("worsened"))
        {
            return "这段时间训练表现有好有坏";
        }
        if (verdicts.Contains("improved"))
        {
            return "这段时间训练表现有进步";
        }
        if (verdicts.Contains("worsened"))
        {
            return "这段时间部分训练表现有所下降";
        }
        return "这段时间训练表现总体平稳";
    }

    private static string OverallStatusClass(
        IReadOnlyCollection<IReGoCapabilityEvaluationDto> evaluations)
    {
        if (evaluations.Count == 0)
        {
            return "insufficient";
        }
        var verdicts = evaluations.Select(x => x.Verdict)
            .ToHashSet(StringComparer.Ordinal);
        if (verdicts.Contains("mixed") ||
            verdicts.Contains("improved") && verdicts.Contains("worsened"))
        {
            return "mixed";
        }
        if (verdicts.Contains("improved"))
        {
            return "improved";
        }
        if (verdicts.Contains("worsened"))
        {
            return "worsened";
        }
        return "stable";
    }

    private static string BuildPerformanceSummary(
        IReadOnlyCollection<IReGoCapabilityEvaluationDto> evaluations)
    {
        if (evaluations.Count == 0)
        {
            return "目前还没有达到评价所需的可比较训练记录数量。";
        }

        var clauses = evaluations
            .Select(x => CapabilityClause(
                x.CapabilityCode,
                x.CapabilityName,
                x.Verdict))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal)
            .Take(4)
            .ToArray();
        return clauses.Length == 0
            ? "当前训练表现已按既定规则完成评价。"
            : $"您在训练中{JoinChinese(clauses)}。";
    }

    private static string BuildLifeSummary(
        IReadOnlyCollection<IReGoCapabilityEvaluationDto> evaluations)
    {
        var activities = evaluations
            .Select(x => LifeActivity(x.CapabilityCode))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var parts = new List<string>();
        if (activities.Length > 0)
        {
            parts.Add($"这些训练与{JoinChinese(activities)}有关。");
        }
        return parts.Count == 0
            ? "本结论只描述所选范围内的训练表现。"
            : string.Join(string.Empty, parts);
    }

    private static string CapabilityClause(
        string capabilityCode,
        string capabilityName,
        string verdict) => (capabilityCode, verdict) switch
        {
            ("walking_ability", "improved") => "走得更快",
            ("walking_ability", "stable") => "走路速度变化不大",
            ("walking_ability", "worsened") => "走路速度有所下降",
            ("walking_ability", _) => "走路速度有升有降",
            ("walking_rhythm", "improved") => "走路节奏更稳定",
            ("walking_rhythm", "stable") => "走路节奏变化不大",
            ("walking_rhythm", "worsened") => "走路节奏稳定性有所下降",
            ("walking_rhythm", _) => "走路节奏表现有好有坏",
            ("stepping_control", "improved") => "迈步控制更好",
            ("stepping_control", "stable") => "迈步控制变化不大",
            ("stepping_control", "worsened") => "迈步控制表现有所下降",
            ("stepping_control", _) => "迈步控制表现有好有坏",
            ("sit_stand_ability", "improved") => "起身和站立更顺",
            ("sit_stand_ability", "stable") => "起身和站立表现变化不大",
            ("sit_stand_ability", "worsened") => "起身和站立表现有所下降",
            ("sit_stand_ability", _) => "起身和站立表现有好有坏",
            ("balance_ability", "improved") => "站立时身体控制更稳",
            ("balance_ability", "stable") => "站立时身体控制变化不大",
            ("balance_ability", "worsened") => "站立时身体控制有所下降",
            ("balance_ability", _) => "站立时身体控制表现有好有坏",
            ("weight_shift_ability", "improved") => "移动重心时更稳",
            ("weight_shift_ability", "stable") => "移动重心表现变化不大",
            ("weight_shift_ability", "worsened") => "移动重心表现有所下降",
            ("weight_shift_ability", _) => "移动重心表现有好有坏",
            ("game_training_performance", "improved") =>
                "完成游戏任务更有效率",
            ("game_training_performance", "stable") =>
                "完成游戏任务的效率变化不大",
            ("game_training_performance", "worsened") =>
                "完成游戏任务的效率有所下降",
            ("game_training_performance", _) =>
                "完成游戏任务的表现有好有坏",
            ("active_walking_participation", "improved") =>
                "能在更少设备支持下完成步行训练",
            ("walking_load_tolerance", "improved") =>
                "能在更高步行负荷下完成训练",
            ("active_sit_stand_participation", "improved") =>
                "能以更主动的方式完成起身训练",
            (_, "improved") => $"{capabilityName}有所改善",
            (_, "stable") => $"{capabilityName}变化不大",
            (_, "worsened") => $"{capabilityName}表现有所下降",
            _ => $"{capabilityName}表现有好有坏"
        };

    private static string? LifeActivity(string capabilityCode) =>
        capabilityCode switch
        {
            "walking_ability" or "walking_rhythm" or "stepping_control" or
                "active_walking_participation" or "walking_load_tolerance" =>
                "日常走路、短距离移动和转弯",
            "sit_stand_ability" or "active_sit_stand_participation" =>
                "从椅子上起身和床椅转移",
            "balance_ability" or "weight_shift_ability" =>
                "短时间站立、转身和站着取物",
            "game_training_performance" =>
                "持续注意、及时反应和手脚配合完成任务",
            _ => null
        };

    private static string StatusLabel(string verdict) => verdict switch
    {
        "improved" => "改善",
        "stable" => "无明显变化",
        "worsened" => "表现下降",
        "mixed" => "表现有好有坏",
        _ => "数据不足"
    };

    private static string VerdictClass(string verdict) => verdict switch
    {
        "improved" => "improved",
        "stable" => "stable",
        "worsened" => "worsened",
        "mixed" => "mixed",
        _ => "insufficient"
    };

    private static string OverviewLifeHint(
        IReGoCapabilityEvaluationDto evaluation)
    {
        var activity = LifeActivity(evaluation.CapabilityCode) ?? "相关日常活动";
        return evaluation.Verdict switch
        {
            "improved" => $"{activity}可能会更顺畅。",
            "stable" => $"{activity}的相关训练表现保持平稳。",
            "worsened" => $"{activity}的相关训练表现有所下降，需要多加留意。",
            "mixed" => $"{activity}的相关训练表现还不稳定。",
            _ => $"{activity}的可比较训练记录不足。"
        };
    }

    private static bool HasEnoughValidRecords(
        IReGoLongitudinalMetricSeriesDto series) =>
        series.Points.Count(x =>
            x.Status == "valid" && x.NormalizedValue.HasValue) >= 3;

    private static bool IsRenderableSeries(
        IReGoLongitudinalProjectDto project,
        IReGoLongitudinalMetricSeriesDto series)
    {
        if (!HasEnoughValidRecords(series))
        {
            return false;
        }
        if (project.InteractionType is "game" or "vr_game" &&
            series.MetricCode is "game_difficulty" or "game_mode")
        {
            return false;
        }
        return true;
    }

    private static IReadOnlyList<ChartGroup> BuildChartGroups(
        IReadOnlyList<IReGoLongitudinalMetricSeriesDto> series)
    {
        var groups = new List<List<IReGoLongitudinalMetricSeriesDto>>();
        foreach (var item in series)
        {
            if (item.MetricCode == "game_score")
            {
                groups.Add(new List<IReGoLongitudinalMetricSeriesDto> { item });
                continue;
            }

            var compatible = groups.FirstOrDefault(group =>
                group.Count < 8 &&
                group.All(x => x.MetricCode != "game_score") &&
                string.Equals(
                    group[0].Unit ?? string.Empty,
                    item.Unit ?? string.Empty,
                    StringComparison.Ordinal));
            if (compatible == null)
            {
                groups.Add(new List<IReGoLongitudinalMetricSeriesDto> { item });
            }
            else
            {
                compatible.Add(item);
            }
        }
        return groups.Select(group => new ChartGroup(
                group,
                group.Select(x => x.Unit)
                    .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))))
            .ToArray();
    }

    private static string JoinChinese(IReadOnlyList<string> values) =>
        values.Count switch
        {
            0 => string.Empty,
            1 => values[0],
            2 => values[0] + "和" + values[1],
            _ => string.Join("、", values.Take(values.Count - 1)) +
                "，以及" + values[^1]
        };

    private sealed record ChartGroup(
        IReadOnlyList<IReGoLongitudinalMetricSeriesDto> Series,
        string? Unit);

    private static string BuildChart(
        ChartGroup group,
        IReadOnlyList<IReGoLongitudinalReportPositionDto> reports,
        IReGoLongitudinalProjectDto project,
        IReadOnlyList<IReGoLongitudinalMetricSeriesDto> contextSeries)
    {
        const decimal width = 960m;
        const decimal height = 232m;
        const decimal left = 54m;
        const decimal right = 34m;
        const decimal top = 26m;
        var gameScore = group.Series.Any(x => x.MetricCode == "game_score");
        var bottom = gameScore ? 60m : 45m;
        var numeric = group.Series
            .SelectMany(series => series.Points)
            .Where(point =>
                point.Status == "valid" && point.NormalizedValue.HasValue)
            .Select(point => point.NormalizedValue!.Value)
            .ToArray();
        var minimum = numeric.Length == 0 ? 0m : numeric.Min();
        var maximum = numeric.Length == 0 ? 1m : numeric.Max();
        if (maximum == minimum)
        {
            minimum -= 0.5m;
            maximum += 0.5m;
        }
        var padding = Math.Max((maximum - minimum) * 0.12m, 0.01m);
        minimum -= padding;
        maximum += padding;
        decimal X(int index) => reports.Count == 1
            ? width / 2m
            : left + index * (width - left - right) / (reports.Count - 1m);
        decimal Y(decimal value) => top +
            (maximum - value) * (height - top - bottom) / (maximum - minimum);
        string N(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);
        var labelStep = Math.Max(1, (int)Math.Ceiling(reports.Count / 10m));
        bool ShowLabel(int index) => index == reports.Count - 1 ||
            index % labelStep == 0 && index <= reports.Count - 1 - labelStep;

        var colors = new[]
        {
            "#2c8d83", "#347fa8", "#7768c7", "#cf8d2f", "#c35d64"
        };
        var svg = new StringBuilder();
        svg.Append("<div class=\"trend-chart-row")
            .Append(group.Series.Count > 1 ? " has-legend" : string.Empty)
            .Append("\"><div class=\"chart\"><svg viewBox=\"0 0 960 232\" role=\"img\" aria-label=\"")
            .Append(E(string.Join("、", group.Series.Select(x => x.DisplayName))))
            .Append("跨报告折线图\">");
        for (var grid = 0; grid < 4; grid++)
        {
            var value = minimum + (maximum - minimum) * grid / 3m;
            var y = Y(value);
            svg.Append("<line class=\"grid\" x1=\"").Append(N(left)).Append("\" y1=\"")
                .Append(N(y)).Append("\" x2=\"").Append(N(width - right)).Append("\" y2=\"")
                .Append(N(y)).Append("\"/><text class=\"axis\" x=\"").Append(N(left - 8m))
                .Append("\" y=\"").Append(N(y + 4m)).Append("\" text-anchor=\"end\">")
                .Append(value.ToString("0.##", CultureInfo.InvariantCulture)).Append("</text>");
        }

        for (var seriesIndex = 0;
             seriesIndex < group.Series.Count;
             seriesIndex++)
        {
            var series = group.Series[seriesIndex];
            var color = colors[seriesIndex % colors.Length];
            var previousValid = -1;
            for (var i = 0; i < reports.Count; i++)
            {
                var point = PointForReport(series, reports[i], i);
                if (point == null || point.Status != "valid" ||
                    !point.NormalizedValue.HasValue)
                {
                    continue;
                }
                if (previousValid >= 0)
                {
                    var previous = PointForReport(
                        series,
                        reports[previousValid],
                        previousValid)!;
                    svg.Append("<line class=\"series\" style=\"stroke:")
                        .Append(color).Append("\" x1=\"")
                        .Append(N(X(previousValid))).Append("\" y1=\"")
                        .Append(N(Y(previous.NormalizedValue!.Value))).Append("\" x2=\"")
                        .Append(N(X(i))).Append("\" y2=\"")
                        .Append(N(Y(point.NormalizedValue.Value))).Append("\"/>");
                }
                previousValid = i;
            }

            for (var i = 0; i < reports.Count; i++)
            {
                var point = PointForReport(series, reports[i], i);
                if (point == null || point.Status != "valid" ||
                    !point.NormalizedValue.HasValue)
                {
                    continue;
                }
                var x = X(i);
                var y = Y(point.NormalizedValue.Value);
                var displayValue = point.DisplayValue ??
                    point.NormalizedValue.Value.ToString(
                        "0.###",
                        CultureInfo.InvariantCulture);
                svg.Append("<circle class=\"point\" style=\"fill:")
                    .Append(color).Append("\" cx=\"").Append(N(x))
                    .Append("\" cy=\"").Append(N(y)).Append("\" r=\"6\"><title>")
                    .Append(E(series.DisplayName)).Append("：")
                    .Append(E(displayValue)).Append("</title></circle>");
                if (group.Series.Count <= 2 && ShowLabel(i))
                {
                    var labelOffset = 12m + seriesIndex * 11m;
                    svg.Append("<text class=\"value-label\" style=\"fill:")
                        .Append(color).Append("\" x=\"").Append(N(x))
                        .Append("\" y=\"").Append(N(y - labelOffset))
                        .Append("\" text-anchor=\"middle\">")
                        .Append(E(displayValue)).Append("</text>");
                }
            }
        }

        var scoreSeries = group.Series.FirstOrDefault(x =>
            x.MetricCode == "game_score");
        var modeSeries = contextSeries.FirstOrDefault(x =>
            x.MetricCode == "game_mode");
        for (var i = 0; i < reports.Count; i++)
        {
            var report = reports[i];
            if (!ShowLabel(i)) continue;
            var x = X(i);
            svg.Append("<text class=\"axis report-label\" x=\"")
                .Append(N(x)).Append("\" y=\"")
                .Append(N(gameScore ? height - 29m : height - 15m))
                .Append("\" text-anchor=\"middle\">")
                .Append(E(FormatDate(report.SessionTime))).Append("</text>");
            if (gameScore && scoreSeries != null)
            {
                var scorePoint = PointForReport(scoreSeries, report, i);
                var modePoint = modeSeries == null
                    ? null
                    : PointForReport(modeSeries, report, i);
                var mode = modePoint?.DisplayValue ??
                    modePoint?.NormalizedValue?.ToString(
                        "0.###",
                        CultureInfo.InvariantCulture) ??
                    project.GameMode?.ToString(CultureInfo.InvariantCulture);
                var labels = new List<string>();
                if (scorePoint?.GameDifficulty.HasValue == true)
                {
                    labels.Add($"难度{scorePoint.GameDifficulty.Value}");
                }
                if (!string.IsNullOrWhiteSpace(mode))
                {
                    labels.Add($"模式{mode}");
                }
                if (labels.Count > 0)
                {
                    svg.Append("<text class=\"axis game-label\" x=\"")
                        .Append(N(x)).Append("\" y=\"")
                        .Append(N(height - 13m))
                        .Append("\" text-anchor=\"middle\">")
                        .Append(E(string.Join(" · ", labels))).Append("</text>");
                }
            }
        }
        svg.Append("</svg></div>");
        if (group.Series.Count > 1)
        {
            svg.Append("<div class=\"trend-legend\">");
            for (var seriesIndex = 0;
                 seriesIndex < group.Series.Count;
                 seriesIndex++)
            {
                svg.Append("<span><i style=\"background:")
                    .Append(colors[seriesIndex % colors.Length])
                    .Append("\"></i>")
                    .Append(E(group.Series[seriesIndex].DisplayName))
                    .Append("</span>");
            }
            svg.Append("</div>");
        }
        svg.Append("</div>");
        return svg.ToString();
    }

    private static IReGoLongitudinalMetricPointDto? PointForReport(
        IReGoLongitudinalMetricSeriesDto series,
        IReGoLongitudinalReportPositionDto report,
        int reportIndex) => series.Points.FirstOrDefault(x =>
            x.ReportOrdinal == report.Ordinal) ??
            (reportIndex < series.Points.Count ? series.Points[reportIndex] : null);

    private static string SegmentSummary(IReGoLongitudinalLaneDto lane)
    {
        var present = lane.Segments.Count(x => x.Status == "present");
        var missing = lane.Segments.Count - present;
        return missing == 0
            ? $"{present}份报告均有记录"
            : $"有记录{present}份 · 缺失{missing}份";
    }

    private static string ProjectClass(IReGoLongitudinalProjectDto project) =>
        project.InteractionType is "game" or "vr_game"
            ? "game"
            : project.RehabContent switch
            {
                "walking" or "single_walking" => "walking",
                "sit_stand" or "single_sit" => "sit",
                "balance_training" or "balance_assessment" => "balance",
                _ => "other"
            };

    private static string SectionHead(
        string eyebrow,
        string title,
        string note) =>
        $"<section class=\"section\"><div class=\"head\"><div><small>{E(eyebrow)}</small><h2>{E(title)}</h2></div><p>{E(note)}</p></div>";

    private static string Fact(string label, string value) =>
        $"<div class=\"fact\"><span>{E(label)}</span><strong>{E(value)}</strong></div>";

    private static string FormatDate(DateTimeOffset? value) =>
        value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "时间缺失";

    private static string FormatTime(DateTimeOffset? value) =>
        value?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ??
        "时间缺失";

    private static string E(string? value) =>
        WebUtility.HtmlEncode(value ?? string.Empty);

    internal static string VisualStyles => Styles;

    private const string Styles = @"
        :root{--ink:#17384d;--muted:#6b8190;--line:#d8e6eb;--wash:#f3f8fa;--blue:#2c86b2;--teal:#32998b;--violet:#7669c8;--gray:#aebbc1;--green:#187f6c}
        *{box-sizing:border-box}html,body{margin:0;background:#eaf3f7;color:var(--ink);font-family:'Microsoft YaHei','PingFang SC',sans-serif}body{padding:30px}.report{width:1120px;margin:auto;background:#fff;border-radius:28px;overflow:hidden;box-shadow:0 24px 70px rgba(25,70,95,.15)}
        .hero{padding:42px 49px 36px;color:#fff;background:linear-gradient(112deg,#194e6b,#29849f 55%,#5e73bf)}.eyebrow{font-size:11px;letter-spacing:2.5px;color:#d9edf5;font-weight:800}.hero h1{font-size:40px;margin:11px 0 7px}.hero p{margin:0;color:#e1eff6}.facts{display:grid;grid-template-columns:repeat(4,1fr);gap:11px;margin-top:28px}.fact{padding:13px 14px;border:1px solid rgba(255,255,255,.17);border-radius:14px;background:rgba(255,255,255,.11)}.fact span{display:block;font-size:11px;color:#deedf5;margin-bottom:6px}.fact strong{font-size:17px}
        main{padding:36px 46px 24px}.section{padding-bottom:32px;margin-bottom:31px;border-bottom:1px solid var(--line)}.head{display:flex;justify-content:space-between;align-items:end;margin-bottom:20px}.head small{display:block;color:var(--blue);font-weight:800;letter-spacing:2px;margin-bottom:5px}.head h2{font-size:26px;margin:0}.head p{max-width:520px;margin:0;color:var(--muted);font-size:12px;line-height:1.7;text-align:right}
        .timeline{display:grid;grid-template-columns:repeat(2,1fr);gap:13px}.visit{display:grid;grid-template-columns:62px 1fr auto;align-items:center;gap:16px;padding:16px 18px;border:1px solid var(--line);border-radius:17px;background:#fbfdfe}.visit-number{font-size:31px;line-height:1;font-weight:800;text-align:center}.visit.complete .visit-number{color:#708f88}.visit.incomplete .visit-number{color:#aeb8bd}.visit b{display:block;font-size:15px}.visit p{margin:5px 0 0;color:var(--muted);font-size:11px}.visit em{padding:5px 9px;border-radius:99px;font-size:10px;font-style:normal;font-weight:700}.visit.complete em{background:#e6f5f1;color:var(--green)}.visit.incomplete em{background:#eceff1;color:#7f8c92}.smart-eval{margin-top:18px;border:1px solid #cfe2e3;border-radius:18px;overflow:hidden;background:linear-gradient(135deg,#f5fbfa,#f5f8fd)}.evaluation-heading{display:flex;align-items:center;justify-content:space-between;gap:12px;padding:15px 18px;border-bottom:1px solid var(--line)}.evaluation-heading h3{margin:0;font-size:16px}.ai-rule-badge{padding:5px 10px;border-radius:99px;background:#e2f2ee;color:#257463;font-size:10px;font-weight:800}.overview-summary{display:grid;grid-template-columns:205px 1fr;gap:18px;align-items:start;padding:18px}.overview-summary strong{display:block;font-size:16px;line-height:1.7}.overview-summary p{margin:7px 0 0;color:var(--muted);font-size:11px;line-height:1.8}.status-badge{display:block;padding:10px 12px;border-radius:12px;text-align:center;font-size:12px;font-weight:800}.status-badge.improved{background:#dff3eb;color:#167763}.status-badge.stable{background:#e7f0f4;color:#426a7c}.status-badge.worsened{background:#fae8e5;color:#a04e43}.status-badge.mixed{background:#fff0d7;color:#9b6a19}.status-badge.insufficient{background:#eceff1;color:#74858d}.overview-eval-grid{display:grid;grid-template-columns:repeat(2,1fr);gap:10px;padding:0 18px 18px}.overview-eval-item{padding:13px 14px;border:1px solid #dce8eb;border-left:4px solid #78909c;border-radius:12px;background:#fff}.overview-eval-item.improved{border-left-color:#2f9b82}.overview-eval-item.stable{border-left-color:#6d94a5}.overview-eval-item.worsened{border-left-color:#c66a5c}.overview-eval-item.mixed{border-left-color:#d09a3a}.overview-eval-item.insufficient{border-left-color:#aab7bd;background:#f4f6f7}.overview-eval-item b{font-size:12px}.overview-eval-item p{margin:5px 0 0;color:#657b85;font-size:10px;line-height:1.65}
        .module{margin-top:16px;border:1px solid var(--line);border-radius:20px;overflow:hidden;background:#fff}.module-head{display:flex;justify-content:space-between;align-items:center;padding:18px 20px;color:#fff}.module-head.walking{background:linear-gradient(135deg,#277fa9,#51a9bc)}.module-head.sit{background:linear-gradient(135deg,#278c7d,#58a98f)}.module-head.game{background:linear-gradient(135deg,#6258b3,#8d7fd0)}.module-head.balance{background:linear-gradient(135deg,#b0772f,#d0a45b)}.module-head.other{background:linear-gradient(135deg,#566f7d,#78909c)}.module-head small{display:block;font-size:10px;letter-spacing:1.3px;opacity:.87}.module-head h3{margin:5px 0 0;font-size:22px}.module-head span{font-size:11px;opacity:.9}.metric-list{padding:4px 18px 18px}.metric-card{margin-top:14px;border:1px solid var(--line);border-radius:15px;overflow:hidden}.metric-title{display:flex;justify-content:space-between;align-items:center;padding:13px 16px;background:#f7fafb}.metric-title h4{margin:0;font-size:14px}.metric-title span{color:var(--muted);font-size:10px}.trend-chart-row.has-legend{display:grid;grid-template-columns:minmax(0,1fr) 190px;gap:12px;align-items:center}.trend-legend{display:flex;flex-direction:column;gap:9px;padding:14px 14px 14px 0;color:#536c76;font-size:10px;line-height:1.4}.trend-legend span{display:flex;align-items:flex-start;gap:7px}.trend-legend i{width:10px;height:10px;margin-top:2px;border-radius:50%;flex:0 0 10px}.chart{padding:8px 12px 0}.chart svg{display:block;width:100%;height:auto}.grid{stroke:#e4ecef;stroke-width:1}.axis{fill:#778b95;font-size:10px}.series{stroke:var(--teal);stroke-width:3;fill:none}.point{fill:var(--teal);stroke:#fff;stroke-width:2}.missing-point{fill:#c7d0d4;stroke:#fff;stroke-width:2}.value-label{fill:#3c5966;font-size:11px;font-weight:700}.missing-label{fill:#8f9ba1;font-size:10px}.report-label{font-weight:700}.date-label{font-size:9px}.empty{padding:24px;color:var(--muted);text-align:center}
        .evaluation-module{background:#f9fcfc}.project-evaluation-heading{background:#eef6f5}.layered-evaluation{margin-top:14px;border:1px solid var(--line);border-left:4px solid #78909c;border-radius:15px;overflow:hidden;background:#fff}.layered-evaluation.improved{border-left-color:#2f9b82}.layered-evaluation.stable{border-left-color:#6d94a5}.layered-evaluation.worsened{border-left-color:#c66a5c}.layered-evaluation.mixed{border-left-color:#d09a3a}.layered-evaluation.insufficient{border-left-color:#aab7bd}.layer{display:grid;grid-template-columns:92px 1fr;gap:12px;padding:12px 15px;font-size:11px;line-height:1.75}.layer+.layer{border-top:1px solid var(--line)}.layer strong{color:#315e68}.layer.detail{background:#f7fafb}.layer.life{background:#f1f8f6}.evaluation-status{display:inline-block;margin-right:7px;padding:2px 7px;border-radius:99px;background:#e7f1f2;color:#3b6c75;font-size:9px;font-style:normal;font-weight:800}.evaluation-boundary{display:block;padding:9px 15px;border-top:1px dashed var(--line);color:#7b8e97;font-size:9px;line-height:1.6}
        .notice{margin:0 46px 28px;padding:17px 20px;border:1px solid var(--line);border-radius:15px;background:var(--wash);font-size:12px;line-height:1.8;color:#607786}.notice strong{display:block;color:var(--ink)}footer{display:flex;justify-content:space-between;padding:18px 46px;border-top:1px solid var(--line);color:#7b909c;font-size:10px}
        @media(max-width:850px){body{padding:0}.report{width:100%;border-radius:0}.facts,.timeline,.overview-summary,.overview-eval-grid{grid-template-columns:1fr}.visit{grid-template-columns:48px 1fr}.visit em{grid-column:2}main{padding:28px 20px}.notice{margin-left:20px;margin-right:20px}.head{display:block}.head p{text-align:left;margin-top:8px}.trend-chart-row.has-legend{display:block}.trend-legend{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));padding:4px 14px 14px}}";
}
