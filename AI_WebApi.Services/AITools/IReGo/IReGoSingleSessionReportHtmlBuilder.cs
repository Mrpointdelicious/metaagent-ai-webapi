using System.Globalization;
using System.Net;
using System.Text;
using AI_WebApi.IServices.AITools;
using AI_WebApi.Model.DTOs.AITools;

namespace AI_WebApi.Services.AITools.IReGo;

public sealed class IReGoSingleSessionReportHtmlBuilder :
    IIReGoSingleSessionReportHtmlBuilder
{
    private static readonly IReadOnlyDictionary<string, string> MetricLabels =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["walking_mode"] = "训练模式",
            ["walking_set_time"] = "设置训练时长",
            ["walking_force"] = "步行力",
            ["walking_training_duration"] = "训练时长",
            ["walking_distance"] = "训练距离",
            ["walking_step_count"] = "步数",
            ["walking_average_speed"] = "平均速度",
            ["walking_average_speed_derived"] = "平均步行速度",
            ["walking_max_speed"] = "最大速度",
            ["walking_average_frequency"] = "平均频率",
            ["walking_step_frequency"] = "平均步频",
            ["walking_step_length"] = "平均步长",
            ["walking_step_time"] = "平均步时",
            ["walking_step_width"] = "平均步宽",
            ["walking_stance_average"] = "支撑相平均值",
            ["walking_swing_average"] = "摆动相平均值",
            ["walking_double_support_average"] = "双支撑相平均值",
            ["walking_unloading"] = "减重量",
            ["walking_average_gait_period"] = "平均步态周期",
            ["walking_average_stride"] = "平均步幅",
            ["walking_display_time"] = "记录时长",
            ["walking_display_set_time"] = "设置训练时长",
            ["walking_display_assistance"] = "助力",
            ["walking_display_resistance"] = "阻力",
            ["walking_display_weight"] = "减重量",
            ["sit_stand_mode"] = "训练模式",
            ["sit_stand_set_time"] = "设置训练时长",
            ["sit_stand_planned_repetitions"] = "设置训练次数",
            ["sit_stand_unloading"] = "减重量",
            ["sit_stand_training_duration"] = "训练时长",
            ["sit_stand_repetitions"] = "训练次数",
            ["sit_stand_cycle_average"] = "平均坐站周期",
            ["sit_stand_cycle_maximum"] = "最大坐站周期",
            ["sit_stand_cycle_minimum"] = "最小坐站周期",
            ["sit_stand_stability_up_time"] = "起立稳定时间",
            ["sit_stand_stability_down_time"] = "坐下稳定时间",
            ["sit_stand_average_time"] = "平均时间",
            ["sit_stand_min_time"] = "最短时间",
            ["sit_stand_max_time"] = "最长时间",
            ["sit_stand_average_speed"] = "平均坐站速度",
            ["sit_stand_average_distance"] = "平均坐站距离",
            ["sit_display_hold_time"] = "坐位保持时间",
            ["stand_display_hold_time"] = "站立保持时间",
            ["sit_stand_display_speed"] = "坐站速度",
            ["sit_stand_display_unloading"] = "减重量",
            ["game_mode"] = "游戏模式",
            ["game_set_time"] = "设置训练时长",
            ["game_difficulty"] = "训练难度",
            ["game_score"] = "训练得分",
            ["game_score_rate"] = "单位时间得分",
            ["game_training_duration"] = "训练时长",
            ["game_star"] = "获得星数",
            ["balance_mode"] = "平衡模式",
            ["balance_set_or_record_time"] = "设置训练时长",
            ["balance_unloading"] = "减重量",
            ["balance_training_duration"] = "训练时长",
            ["balance_trajectory_length"] = "轨迹长度",
            ["balance_trajectory_area"] = "轨迹面积",
            ["balance_front_back_average"] = "前后平均偏移",
            ["balance_left_right_average"] = "左右平均偏移",
            ["balance_max_front_lean"] = "向前最大偏移",
            ["balance_max_back_lean"] = "向后最大偏移",
            ["balance_max_left_lean"] = "向左最大偏移",
            ["balance_max_right_lean"] = "向右最大偏移",
            ["balance_average_amplitude_front_back"] = "前后平均摆幅",
            ["balance_average_amplitude_left_right"] = "左右平均摆幅",
            ["balance_max_amplitude_front_back"] = "前后最大摆幅",
            ["balance_max_amplitude_left_right"] = "左右最大摆幅",
            ["balance_average_speed_front_back"] = "前后平均速度",
            ["balance_average_speed_left_right"] = "左右平均速度",
            ["balance_data_area"] = "活动面积",
            ["romberg_area"] = "Romberg面积",
            ["romberg_length"] = "Romberg轨迹长度",
            ["vestibular_length"] = "前庭轨迹长度",
            ["vestibular_average_speed"] = "前庭平均速度",
            ["vestibular_area"] = "前庭活动面积",
            ["vestibular_average_swing"] = "前庭平均摆幅",
            ["vestibular_max_swing_x"] = "前庭左右最大摆幅",
            ["vestibular_max_swing_y"] = "前庭前后最大摆幅",
            ["vision_open_length"] = "睁眼轨迹长度",
            ["vision_close_length"] = "闭眼轨迹长度",
            ["vision_open_area"] = "睁眼活动面积",
            ["vision_close_area"] = "闭眼活动面积",
            ["romberg_length_recomputed"] = "Romberg轨迹长度比",
            ["romberg_area_recomputed"] = "Romberg面积比",
            ["proprio_both_max_swing_x"] = "本体双足左右最大摆幅",
            ["proprio_both_max_swing_y"] = "本体双足前后最大摆幅",
            ["stability_field"] = "稳定范围",
            ["plan_walking_mode"] = "训练模式",
            ["plan_set_time"] = "设置训练时长",
            ["plan_assistance"] = "主动助力",
            ["plan_resistance"] = "阻力",
            ["plan_weight_loss"] = "减重量",
            ["plan_walking_distance"] = "设置训练距离",
            ["plan_sit_stand_mode"] = "训练模式",
            ["plan_sit_stand_repetitions"] = "设置训练次数",
            ["plan_game_mode"] = "游戏模式",
            ["plan_game_intensity"] = "训练强度",
            ["plan_balance_mode"] = "平衡模式",
            ["plan_walking_speed"] = "步行速度",
            ["plan_speed"] = "训练速度",
            ["plan_sit_hold_duration"] = "坐位保持时间",
            ["plan_stand_hold_duration"] = "站立保持时间"
        };

    private static readonly string[] Colors =
    {
        "#2789b5", "#249b91", "#f2a51f", "#7669c8", "#db6d77",
        "#4d7fb9", "#55a968", "#b879c6"
    };

    private static readonly IReadOnlySet<string> ComparisonMetricCodes =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "plan_walking_mode", "plan_set_time", "plan_assistance",
            "plan_resistance", "plan_weight_loss", "plan_walking_distance",
            "plan_sit_stand_mode", "plan_sit_stand_repetitions",
            "plan_game_mode", "plan_game_intensity", "plan_balance_mode",
            "plan_walking_speed", "plan_speed", "plan_sit_hold_duration",
            "plan_stand_hold_duration",
            "walking_mode", "walking_set_time", "walking_force",
            "walking_unloading", "walking_display_set_time",
            "walking_display_assistance", "walking_display_resistance",
            "walking_display_weight", "sit_stand_mode", "sit_stand_set_time",
            "sit_stand_planned_repetitions", "sit_stand_unloading",
            "sit_display_hold_time", "stand_display_hold_time",
            "sit_stand_display_speed", "sit_stand_display_unloading",
            "game_mode", "game_set_time", "game_difficulty",
            "balance_mode", "balance_set_or_record_time", "balance_unloading"
        };

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>>
        ActualMetricCodesByPlanMetric =
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
            {
                ["plan_walking_mode"] = new[] { "walking_mode" },
                ["plan_set_time"] = new[]
                {
                    "walking_set_time", "walking_display_set_time",
                    "sit_stand_set_time", "game_set_time",
                    "balance_set_or_record_time"
                },
                ["plan_assistance"] = new[]
                {
                    "walking_display_assistance", "walking_force"
                },
                ["plan_resistance"] = new[] { "walking_display_resistance" },
                ["plan_weight_loss"] = new[]
                {
                    "walking_display_weight", "walking_unloading",
                    "sit_stand_unloading", "sit_stand_display_unloading",
                    "balance_unloading"
                },
                ["plan_walking_distance"] = new[] { "walking_distance" },
                ["plan_sit_stand_mode"] = new[] { "sit_stand_mode" },
                ["plan_sit_stand_repetitions"] = new[]
                {
                    "sit_stand_planned_repetitions"
                },
                ["plan_game_mode"] = new[] { "game_mode" },
                ["plan_game_intensity"] = new[] { "game_difficulty" },
                ["plan_balance_mode"] = new[] { "balance_mode" },
                ["plan_walking_speed"] = new[] { "walking_average_speed" },
                ["plan_speed"] = new[] { "sit_stand_display_speed" },
                ["plan_sit_hold_duration"] = new[] { "sit_display_hold_time" },
                ["plan_stand_hold_duration"] = new[] { "stand_display_hold_time" }
            };

    public string Build(
        IReGoSessionAnalysisDto analysis,
        IReGoSingleSessionAiEvaluationDto aiEvaluation)
    {
        var planBlocks = analysis.Plan.Blocks.OrderBy(x => x.Sequence).ToArray();
        var reportBlocks = analysis.ReportBlocks.OrderBy(x => x.Sequence).ToArray();
        // The parameter is retained for API compatibility. Presentation labels
        // are now computed by the backend and cannot be overridden by an LLM.
        var evaluation = IReGoSingleSessionReportEvaluation.CreateBackend(
            analysis);
        if (!evaluation.IsValid)
        {
            throw new ArgumentException(
                evaluation.ReasonCode ??
                "The AI evaluation does not match the report facts.",
                nameof(aiEvaluation));
        }

        var durations = DurationSlices(reportBlocks);

        var html = new StringBuilder();
        html.Append("<!doctype html><html lang=\"zh-CN\"><head><meta charset=\"utf-8\">");
        html.Append("<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
        html.Append("<style>").Append(VisualStyles)
            .Append("</style></head><body>");
        html.Append("<article class=\"report\">");
        BuildHero(html, analysis, reportBlocks.Length);
        html.Append("<main>");
        BuildOverview(
            html,
            planBlocks.Length,
            reportBlocks.Length,
            evaluation,
            durations);
        BuildComparison(html, analysis, planBlocks, reportBlocks);
        BuildDetails(html, reportBlocks);
        html.Append("<section class=\"disclaimer\"><strong>报告使用说明</strong>");
        html.Append("本报告根据本次训练记录自动生成，仅用于了解训练项目和训练情况，不替代医生或治疗师的诊断与评估。如对训练结果有疑问，请咨询专业人员。</section>");
        html.Append("</main><footer><span>IREGO 单次训练报告</span><span>")
            .Append(E(analysis.Time.ReportRecordedAt?.ToString("yyyy-MM-dd") ?? ""))
            .Append("</span></footer></article></body></html>");
        return AI_WebApi.Services.AITools.PatientContext.RecordTenantPresentation.AddToHtml(html.ToString(), analysis);
    }

    private static void BuildHero(
        StringBuilder html,
        IReGoSessionAnalysisDto analysis,
        int reportCount)
    {
        html.Append("<header class=\"hero\"><div class=\"eyebrow\">IREGO · SINGLE SESSION REPORT</div>");
        html.Append("<h1>单次训练报告</h1><div class=\"hero-sub\">计划训练项目与本次实际训练情况</div>");
        html.Append("<div class=\"hero-meta\">");
        HeroFact(html, "预约时间", FormatTime(analysis.Time.ScheduledAt));
        HeroFact(html, "报告记录时间", FormatTime(analysis.Time.ReportRecordedAt));
        HeroFact(html, "训练状态", "完整完成");
        HeroFact(html, "实际训练项目", $"{reportCount} 项");

        html.Append("</div></header>");
    }

    private static void HeroFact(StringBuilder html, string label, string value) =>
        html.Append("<div><span>").Append(E(label)).Append("</span><strong>")
            .Append(E(value)).Append("</strong></div>");

    private static void BuildOverview(
        StringBuilder html,
        int planCount,
        int reportCount,
        IReGoSingleSessionEvaluationResult evaluation,
        IReadOnlyList<DurationSlice> durations)
    {
        html.Append(SectionHead("01 · 训练总览", "训练总览"));
        html.Append("<div class=\"overview\"><div class=\"panel\"><div class=\"panel-title\">训练项目</div>");
        html.Append("<div class=\"counts\">");
        Count(html, "计划项目", planCount);
        Count(html, "实际训练", reportCount);
        html.Append("</div>");
        var percent = Math.Clamp(evaluation.CompletionRate, 0, 100);
        html.Append("<div class=\"completion-row\"><span>完成率</span><strong>")
            .Append(percent.ToString("0.#", CultureInfo.InvariantCulture))
            .Append("%</strong></div><div class=\"completion-bar\"><i style=\"width:")
            .Append(percent.ToString("0.##", CultureInfo.InvariantCulture))
            .Append("%\"></i></div>");
        BuildSmartEvaluation(html, evaluation);

        html.Append("</div>");
        BuildDurationPanel(html, durations);
        html.Append("</div></section>");
    }

    internal static void BuildDurationPanel(
        StringBuilder html,
        IReadOnlyList<DurationSlice> durations)
    {
        html.Append("<div class=\"panel\"><div class=\"panel-title\">各训练项用时占比</div>");
        if (durations.Count == 0 || durations.Sum(x => x.Value) <= 0)
        {
            html.Append("<div class=\"empty\">暂无可汇总的训练时长记录</div>");
        }
        else
        {
            var total = durations.Sum(x => x.Value);
            var stops = new List<string>();
            decimal cursor = 0;
            for (var index = 0; index < durations.Count; index++)
            {
                var start = cursor / total * 100;
                cursor += durations[index].Value;
                var end = cursor / total * 100;
                stops.Add($"{Colors[index % Colors.Length]} {start:0.##}% {end:0.##}%");
            }

            html.Append("<div class=\"duration-wrap\"><div class=\"donut\" style=\"background:conic-gradient(")
                .Append(string.Join(',', stops)).Append(")\"><div><strong>")
                .Append(E(FormatNumber(total))).Append("</strong><span>总训练时长</span></div></div>");
            html.Append("<div class=\"legend\">");
            for (var index = 0; index < durations.Count; index++)
            {
                var item = durations[index];
                html.Append("<span><i style=\"background:")
                    .Append(Colors[index % Colors.Length]).Append("\"></i>")
                    .Append(E(item.Name)).Append(' ')
                    .Append((item.Value / total * 100).ToString("0.#", CultureInfo.InvariantCulture))
                    .Append("%</span>");
            }

            html.Append("</div></div>");
        }

        html.Append("</div>");
    }

    private static void BuildSmartEvaluation(
        StringBuilder html,
        IReGoSingleSessionEvaluationResult evaluation)
    {
        html.Append("<div class=\"smart-evaluation\"><strong>AI评价</strong><span>");
        html.Append("本次完成率")
            .Append(evaluation.CompletionRate.ToString(
                "0.#",
                CultureInfo.InvariantCulture))
            .Append("%，完成率");
        if (evaluation.CompletionLevel == "boundary")
        {
            html.Append("处于<b>分界值</b>");
        }
        else
        {
            html.Append("<b>")
                .Append(E(evaluation.CompletionLevelLabel))
                .Append("</b>");
        }

        foreach (var game in evaluation.Games)
        {
            html.Append("；")
                .Append(E(game.DisplayName))
                .Append("得分")
                .Append(E(game.Score))
                .Append("分，本次游戏表现评价为<b>")
                .Append(E(game.PerformanceLevelLabel))
                .Append("</b>");
        }

        html.Append("。</span></div>");
    }

    private static void Count(StringBuilder html, string label, int value) =>
        html.Append("<div class=\"count-box\"><span>").Append(E(label))
            .Append("</span><strong>").Append(value).Append("</strong></div>");

    private static void BuildComparison(
        StringBuilder html,
        IReGoSessionAnalysisDto analysis,
        IReadOnlyList<IReGoContentBlockDto> plans,
        IReadOnlyList<IReGoContentBlockDto> reports)
    {
        html.Append(SectionHead("02 · 计划与训练", "计划与实际训练对照"));
        html.Append("<div class=\"compare-header\"><div>第一列 · 计划</div><div>第二列 · 实际训练</div></div>");
        var rows = analysis.BlockComparison.Count > 0
            ? analysis.BlockComparison.OrderBy(x => x.PlanSequence ?? int.MaxValue)
                .ThenBy(x => x.ReportSequence ?? int.MaxValue).ToArray()
            : Enumerable.Range(0, Math.Max(plans.Count, reports.Count))
                .Select(index => new IReGoBlockComparisonDto
                {
                    PlanSequence = index < plans.Count ? plans[index].Sequence : null,
                    ReportSequence = index < reports.Count ? reports[index].Sequence : null
                }).ToArray();

        foreach (var row in rows)
        {
            var plan = plans.FirstOrDefault(x => x.Sequence == row.PlanSequence);
            var report = reports.FirstOrDefault(x => x.Sequence == row.ReportSequence);
            html.Append("<div class=\"compare-row\">");
            BuildComparisonSide(
                html,
                plan,
                "plan-side",
                "未设置计划项目",
                ComparisonMetricCodes);
            BuildComparisonSide(
                html,
                report,
                "actual-side",
                "无对应训练记录",
                ActualComparisonMetricCodes(plan));
            html.Append("</div>");
        }

        html.Append("</section>");
    }

    private static void BuildComparisonSide(
        StringBuilder html,
        IReGoContentBlockDto? block,
        string cssClass,
        string emptyText,
        IReadOnlySet<string> allowedMetricCodes)
    {
        html.Append("<div class=\"compare-side ").Append(cssClass).Append("\">");
        if (block == null)
        {
            html.Append("<div class=\"compare-title muted\"><strong>")
                .Append(E(emptyText)).Append("</strong></div></div>");
            return;
        }

        html.Append("<div class=\"compare-title\"><strong>")
            .Append(E(ShortName(block))).Append("</strong></div>");
        BuildParameterList(
            html,
            block.Metrics,
            "param-list",
            "param",
            allowedMetricCodes);
        html.Append("</div>");
    }

    private static IReadOnlySet<string> ActualComparisonMetricCodes(
        IReGoContentBlockDto? plan)
    {
        if (plan == null)
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var metric in plan.Metrics.Where(Visible))
        {
            if (!ActualMetricCodesByPlanMetric.TryGetValue(
                    metric.MetricCode,
                    out var actualMetricCodes))
            {
                continue;
            }

            result.UnionWith(actualMetricCodes);
        }

        return result;
    }

    private static void BuildDetails(
        StringBuilder html,
        IReadOnlyList<IReGoContentBlockDto> blocks)
    {
        html.Append(SectionHead("03 · 训练详情", "各训练项目详情"));
        html.Append("<div class=\"segment-grid\">");
        foreach (var block in blocks)
        {
            html.Append("<article class=\"segment ").Append(CssClass(block)).Append("\">");
            html.Append("<div class=\"segment-head\"><div><h3>")
                .Append(E(ShortName(block))).Append("</h3><div class=\"segment-sub\">第")
                .Append(block.Sequence).Append("项训练</div></div>");
            var score = block.Metrics.FirstOrDefault(x =>
                x.MetricCode == "game_score" && Visible(x));
            if (score != null)
            {
                html.Append("<div class=\"game-score\"><span>训练得分</span><strong>")
                    .Append(E(Value(score))).Append("</strong></div>");
            }

            html.Append("</div>");
            BuildParameterList(html, block.Metrics, "field-list", "field");
            html.Append("</article>");
        }

        html.Append("</div></section>");
    }

    private static void BuildParameterList(
        StringBuilder html,
        IReadOnlyList<MetricFactDto> metrics,
        string listClass,
        string itemClass,
        IReadOnlySet<string>? allowedMetricCodes = null)
    {
        var visible = metrics
            .Where(Visible)
            .Where(x => MetricLabels.ContainsKey(x.MetricCode))
            .Where(x => allowedMetricCodes == null ||
                allowedMetricCodes.Contains(x.MetricCode))
            .ToArray();
        if (visible.Length == 0)
        {
            return;
        }

        html.Append("<div class=\"").Append(listClass).Append("\">");
        foreach (var metric in visible)
        {
            html.Append("<div class=\"").Append(itemClass).Append("\"><span>")
                .Append(E(MetricLabels[metric.MetricCode])).Append("</span><b>")
                .Append(E(Value(metric))).Append("</b></div>");
        }

        html.Append("</div>");
    }

    private static IReadOnlyList<DurationSlice> DurationSlices(
        IReadOnlyList<IReGoContentBlockDto> blocks)
    {
        var result = new List<DurationSlice>();
        foreach (var block in blocks)
        {
            var duration = block.Metrics.FirstOrDefault(x =>
                x.MetricCode.EndsWith("_training_duration", StringComparison.Ordinal) &&
                Visible(x));
            if (duration == null || !TryDecimal(duration.RawValue, out var value) || value < 0)
            {
                continue;
            }

            result.Add(new DurationSlice(ShortName(block), value));
        }

        return result;
    }

    private static bool Visible(MetricFactDto metric)
    {
        if (metric.ValueStatus != "valid" ||
            metric.RawValue == null && !metric.NormalizedValue.HasValue)
        {
            return false;
        }

        var text = Value(metric);
        return !string.IsNullOrWhiteSpace(text) &&
               !text.Equals("NaN", StringComparison.OrdinalIgnoreCase) &&
               !text.Contains("Infinity", StringComparison.OrdinalIgnoreCase);
    }

    private static string Value(MetricFactDto metric)
    {
        var modeName = ModeName(metric);
        if (modeName != null)
        {
            return modeName;
        }
        return metric.RawValue switch
        {
            null => metric.NormalizedValue?.ToString(
                "0.###",
                CultureInfo.InvariantCulture) ?? string.Empty,
            decimal value => FormatNumber(value),
            double value when double.IsFinite(value) =>
                value.ToString("0.###", CultureInfo.InvariantCulture),
            float value when float.IsFinite(value) =>
                value.ToString("0.###", CultureInfo.InvariantCulture),
            IFormattable value => value.ToString(null, CultureInfo.InvariantCulture),
            _ => metric.RawValue.ToString() ?? string.Empty
        };
    }

    private static string? ModeName(MetricFactDto metric)
    {
        var value = metric.NormalizedValue;
        return (metric.MetricCode, value) switch
        {
            ("walking_mode" or "plan_walking_mode", 0m) => "跟随",
            ("walking_mode" or "plan_walking_mode", 1m) => "助力",
            ("walking_mode" or "plan_walking_mode", 2m) => "阻力",
            ("sit_stand_mode" or "plan_sit_stand_mode", 0m) => "主动",
            ("sit_stand_mode" or "plan_sit_stand_mode", 1m) => "被动",
            _ => null
        };
    }

    private static bool TryDecimal(object? rawValue, out decimal value) =>
        decimal.TryParse(
            rawValue?.ToString(),
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out value);

    private static string ShortName(IReGoContentBlockDto block)
    {
        if (block.InteractionType is "game" or "vr_game" &&
            !string.IsNullOrWhiteSpace(block.DisplayName))
        {
            return block.DisplayName;
        }

        return block.RehabContent switch
        {
            "walking" => "步行训练",
            "sit_stand" => "坐站训练",
            "balance_training" => "平衡训练",
            "balance_assessment" => "平衡评估",
            "single_walking" => "单侧步行训练",
            "single_sit" => "单侧坐站训练",
            "joint_training" => "关节训练",
            _ => "训练记录"
        };
    }

    private static string CssClass(IReGoContentBlockDto block) =>
        block.InteractionType is "game" or "vr_game"
            ? "game"
            : block.RehabContent switch
            {
                "walking" or "single_walking" => "walk",
                "sit_stand" or "single_sit" => "sit",
                "balance_training" or "balance_assessment" => "balance",
                _ => "generic"
            };

    private static string SectionHead(string index, string title) =>
        $"<section class=\"section\"><div class=\"section-head\"><div><div class=\"section-index\">{E(index)}</div><h2>{E(title)}</h2></div></div>";

    private static string FormatTime(DateTimeOffset? value) =>
        value?.ToString("yyyy-MM-dd HH:mm") ?? "未记录";

    private static string FormatNumber(decimal value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture);

    private static string E(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);

    internal sealed record DurationSlice(string Name, decimal Value);

    internal static string VisualStyles =>
        Styles + ".hero-meta{grid-template-columns:repeat(4,1fr)}";

    private const string Styles = @"
*{box-sizing:border-box}html,body{margin:0;background:#eaf4f8;color:#14354c;font-family:'Noto Sans CJK SC','Microsoft YaHei','PingFang SC',sans-serif}body{padding:36px}.report{width:1100px;margin:0 auto;background:#fff;border-radius:28px;overflow:hidden;box-shadow:0 25px 70px rgba(29,72,96,.16)}.hero{padding:46px 52px 38px;color:#fff;background:linear-gradient(110deg,#1d536f 0%,#2786a5 53%,#38a79e 100%);position:relative}.hero:after{content:'';position:absolute;width:310px;height:310px;border:1px solid rgba(255,255,255,.09);border-radius:50%;right:-90px;top:-160px;box-shadow:0 0 0 48px rgba(255,255,255,.035)}.eyebrow{font-size:12px;font-weight:700;letter-spacing:3px;color:#bce8f2}.hero h1{font-size:44px;line-height:1.1;margin:14px 0 8px}.hero-sub{font-size:17px;color:#d9f3f5}.hero-meta{display:grid;grid-template-columns:repeat(5,1fr);gap:12px;margin-top:34px}.hero-meta>div{min-height:68px;padding:13px 15px;border-radius:14px;background:rgba(255,255,255,.12);border:1px solid rgba(255,255,255,.16)}.hero-meta span{display:block;font-size:12px;color:#d8f0f4;margin-bottom:7px}.hero-meta strong{font-size:16px}main{padding:42px 52px 28px}.section{padding:0 0 38px;margin-bottom:34px;border-bottom:1px solid #d8e7ed}.section:last-of-type{border:0}.section-head{display:flex;justify-content:space-between;align-items:end;margin-bottom:24px}.section-index{font-size:12px;font-weight:800;letter-spacing:2px;color:#247eaa;margin-bottom:6px}.section h2{font-size:28px;margin:0;color:#12364e}.overview{display:grid;grid-template-columns:.9fr 1.1fr;gap:18px}.panel{border:1px solid #d7e6ed;border-radius:20px;padding:22px;background:#fbfdfe}.panel-title{font-size:15px;font-weight:800;margin-bottom:18px}.counts{display:grid;grid-template-columns:repeat(2,1fr);gap:10px}.count-box{padding:15px;text-align:center;border-radius:13px;background:#f0f6f9}.count-box span{display:block;color:#698396;font-size:13px}.count-box strong{display:block;margin-top:4px;font-size:28px}.completion-row{display:flex;justify-content:space-between;margin-top:20px;font-size:14px}.completion-row strong{font-size:20px;color:#279c8d}.completion-bar{height:8px;background:#e4eef2;border-radius:8px;margin-top:8px;overflow:hidden}.completion-bar i{display:block;height:100%;border-radius:8px;background:linear-gradient(90deg,#2585ad,#28a395)}.smart-evaluation{margin-top:14px;padding:11px 13px;border:1px solid #dce9ee;border-radius:12px;background:#f5fafb;color:#496779;font-size:12px;line-height:1.7;display:flex;align-items:flex-start;gap:8px}.smart-evaluation strong{color:#1c627d;flex:0 0 auto}.smart-evaluation span{min-width:0}.smart-evaluation b{color:#15836f;font-weight:800}.duration-wrap{display:flex;gap:28px;align-items:center}.donut{width:150px;height:150px;flex:0 0 150px;border-radius:50%;display:grid;place-items:center}.donut>div{width:96px;height:96px;border-radius:50%;background:#fff;display:grid;place-content:center;text-align:center}.donut strong{font-size:30px}.donut span{font-size:11px;color:#78909e}.legend{display:grid;grid-template-columns:1fr 1fr;gap:13px;flex:1}.legend span{font-size:13px;color:#587184}.legend i{display:inline-block;width:9px;height:9px;border-radius:50%;margin-right:7px}.empty{display:grid;place-items:center;min-height:150px;color:#8298a7}.compare-header{display:grid;grid-template-columns:1fr 1fr;border:1px solid #d8e7ed;border-bottom:0;border-radius:16px 16px 0 0;overflow:hidden}.compare-header div{padding:13px 19px;background:#f2f7f9;font-size:12px;font-weight:800;color:#637d8e}.compare-header div+div{border-left:1px solid #d8e7ed}.compare-row{display:grid;grid-template-columns:1fr 1fr;border:1px solid #d8e7ed;border-top:0;overflow:hidden}.compare-row:last-child{border-radius:0 0 16px 16px}.compare-side{padding:20px;min-height:96px}.compare-side+div{border-left:1px solid #d8e7ed}.plan-side{background:#f8fbfd}.actual-side{background:#f8fdfc}.compare-title{font-size:18px;margin-bottom:13px}.compare-title:before{content:'';display:inline-block;width:8px;height:8px;border-radius:50%;background:#2789b5;margin-right:9px}.actual-side .compare-title:before{background:#249b91}.compare-title.muted{color:#8398a5;font-size:14px}.param-list{display:flex;flex-wrap:wrap;gap:7px}.param{display:flex;gap:6px;padding:7px 10px;border:1px solid #dbe8ee;border-radius:999px;background:#fff;font-size:12px;color:#627c8e}.param b{color:#173a50}.segment-grid{display:grid;grid-template-columns:1fr 1fr;gap:16px}.segment{border:1px solid #d9e7ec;border-radius:19px;overflow:hidden;background:#fff}.segment-head{display:flex;justify-content:space-between;align-items:center;padding:18px 20px;background:#eef6fa}.segment.sit .segment-head{background:#eaf8f5}.segment.game .segment-head{background:#fff4da}.segment.balance .segment-head{background:#f1effb}.segment h3{font-size:20px;margin:0}.segment-sub{font-size:11px;color:#78909f;margin-top:5px}.field-list{display:grid;grid-template-columns:1fr 1fr;padding:8px 20px 18px}.field{display:flex;justify-content:space-between;gap:12px;padding:11px 0;border-bottom:1px dashed #e0eaee;font-size:13px}.field:nth-last-child(-n+2){border-bottom:0}.field span{color:#6d8493}.field b{text-align:right;color:#16384e}.game-score{min-width:78px;padding:8px 12px;text-align:center;border-radius:12px;background:#fff}.game-score span{display:block;font-size:10px;color:#9e751e}.game-score strong{display:block;font-size:24px;color:#b67c08}.disclaimer{padding:19px 22px;border:1px solid #d9e7ec;border-radius:16px;background:#f2f7f9;font-size:12px;line-height:1.8;color:#607887}.disclaimer strong{display:block;color:#173a50;font-size:14px;margin-bottom:4px}footer{display:flex;justify-content:space-between;padding:20px 52px;border-top:1px solid #e0eaee;color:#8399a6;font-size:11px}@media(max-width:900px){body{padding:0}.report{width:100%;border-radius:0}.hero-meta{grid-template-columns:1fr 1fr}.overview,.segment-grid{grid-template-columns:1fr}.compare-header,.compare-row{grid-template-columns:1fr}.compare-side+div,.compare-header div+div{border-left:0;border-top:1px solid #d8e7ed}}
";
}
