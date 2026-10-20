using System.Globalization;
using System.Net;
using System.Text;
using AI_WebApi.IServices.AITools;
using AI_WebApi.Model.DTOs.AITools;
using AI_WebApi.Services.AITools.IReGo;

namespace AI_WebApi.Services.AITools.IReTour;

/// <summary>
/// IReTour single-session presentation using the exact IReGo visual system:
/// hero, numbered sections, paired plan/result columns and metric cards.
/// </summary>
public sealed class IReTourSingleSessionReportHtmlBuilder :
    IIReTourSingleSessionReportHtmlBuilder
{
    public string Build(IReTourSessionAnalysisDto analysis)
    {
        var metrics = analysis.Result.Metrics
            .Where(x => x.ValueStatus == "valid" && x.NormalizedValue.HasValue)
            .ToArray();
        var parameters = analysis.Plan.Parameters
            .Where(x => !string.IsNullOrWhiteSpace(x.Value))
            .ToArray();
        var resultAvailable = analysis.Result.ResultState == "summary_available" &&
                              metrics.Length > 0;

        var html = new StringBuilder();
        html.Append("<!doctype html><html lang=\"zh-CN\"><head><meta charset=\"utf-8\">")
            .Append("<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">")
            .Append("<title>IRETOUR 单次训练报告</title><style>")
            .Append(IReGoSingleSessionReportHtmlBuilder.VisualStyles)
            .Append("</style></head><body><article class=\"report\">");

        BuildHero(html, analysis);
        html.Append("<main>");
        BuildOverview(html, analysis, resultAvailable);
        BuildComparison(html, analysis, parameters, metrics);
        BuildDetails(html, analysis, metrics);
        IReTourReadingHtml.Append(html, analysis.Interpretation, analysis.SourceSnapshot);
        html.Append("<section class=\"disclaimer\"><strong>报告使用说明</strong>")
            .Append("本报告根据本次 IReTour 训练记录自动生成，仅用于了解训练项目、记录设置和汇总结果，不替代医生或治疗师的诊断与评估。数值变化不等同于康复效果。</section>")
            .Append("</main><footer><span>IRETOUR 单次训练报告</span><span>")
            .Append(E(FormatTime(analysis.Time.ExecutionEndedAt ??
                                 analysis.Time.SourceRecordCreatedAt)))
            .Append("</span></footer></article></body></html>");
        return html.ToString();
    }

    private static void BuildHero(
        StringBuilder html,
        IReTourSessionAnalysisDto analysis)
    {
        html.Append("<header class=\"hero\"><div class=\"eyebrow\">IRETOUR · SINGLE SESSION REPORT</div>")
            .Append("<h1>单次训练报告</h1><div class=\"hero-sub\">记录设置与本次训练汇总</div>")
            .Append("<div class=\"hero-meta\">");
        HeroFact(html, "开始时间", FormatTime(analysis.Time.ExecutionStartedAt));
        HeroFact(html, "结束时间", FormatTime(analysis.Time.ExecutionEndedAt));
        HeroFact(html, "训练状态", TrainingStateLabel(analysis.Time.TrainingState));
        HeroFact(html, "训练项目", analysis.Activity.DisplayName);
        html.Append("</div></header>");
    }

    private static void BuildOverview(
        StringBuilder html,
        IReTourSessionAnalysisDto analysis,
        bool resultAvailable)
    {
        html.Append(SectionHead("01 · 训练总览", "训练总览"))
            .Append("<div class=\"overview\"><div class=\"panel\"><div class=\"panel-title\">训练项目</div>")
            .Append("<div class=\"counts\">");
        Count(html, "记录项目", 1);
        Count(html, "实际训练", resultAvailable ? 1 : 0);
        var availability = resultAvailable ? 100 : 0;
        html.Append("</div><div class=\"completion-row\"><span>结果记录</span><strong>")
            .Append(resultAvailable ? "可用" : "不可用")
            .Append("</strong></div><div class=\"completion-bar\"><i style=\"width:")
            .Append(availability).Append("%\"></i></div>")
            .Append("<div class=\"smart-evaluation\"><strong>数据说明</strong><span>")
            .Append(resultAvailable
                ? "本次训练已有可读取的汇总结果，以下内容按已确认字段展示。"
                : "本次训练暂无可用于生成报告的汇总结果。")
            .Append("</span></div></div>");
        var durations = analysis.Plan.DurationSeconds is > 0
            ? new[]
            {
                new IReGoSingleSessionReportHtmlBuilder.DurationSlice(
                    analysis.Activity.DisplayName, analysis.Plan.DurationSeconds.Value)
            }
            : Array.Empty<IReGoSingleSessionReportHtmlBuilder.DurationSlice>();
        IReGoSingleSessionReportHtmlBuilder.BuildDurationPanel(html, durations);
        html.Append("</div></section>");
    }

    private static void BuildComparison(
        StringBuilder html,
        IReTourSessionAnalysisDto analysis,
        IReadOnlyList<IReTourPlanParameterDto> parameters,
        IReadOnlyList<IReTourMetricDto> metrics)
    {
        html.Append(SectionHead("02 · 设置与结果", "记录设置与训练结果"))
            .Append("<div class=\"compare-header\"><div>第一列 · 记录设置</div><div>第二列 · 实际训练</div></div>")
            .Append("<div class=\"compare-row\"><div class=\"compare-side plan-side\"><div class=\"compare-title\">")
            .Append(E(analysis.Activity.DisplayName)).Append("</div><div class=\"param-list\">");
        AppendParameters(html, parameters);
        html.Append("</div></div><div class=\"compare-side actual-side\"><div class=\"compare-title\">")
            .Append(E(analysis.Activity.DisplayName)).Append("</div><div class=\"param-list\">");
        AppendMetrics(html, metrics.Take(8));
        html.Append("</div></div></div></section>");
    }

    private static void BuildDetails(
        StringBuilder html,
        IReTourSessionAnalysisDto analysis,
        IReadOnlyList<IReTourMetricDto> metrics)
    {
        html.Append(SectionHead("03 · 训练详情", "训练项目详情"))
            .Append("<div class=\"segment-grid\"><article class=\"segment ")
            .Append(ActivityClass(analysis.Activity.CanonicalCode))
            .Append("\"><div class=\"segment-head\"><div><h3>")
            .Append(E(analysis.Activity.DisplayName))
            .Append("</h3><div class=\"segment-sub\">实际训练汇总指标</div></div></div><div class=\"field-list\">");
        if (metrics.Count == 0)
        {
            html.Append("<div class=\"empty\">暂无可展示的汇总指标</div>");
        }
        else
        {
            foreach (var metric in metrics)
            {
                html.Append("<div class=\"field\"><span>")
                    .Append(E(metric.DisplayName)).Append("</span><b>")
                    .Append(E(FormatMetric(metric))).Append("</b></div>");
            }
        }
        html.Append("</div></article></div></section>");
    }

    private static void AppendParameters(
        StringBuilder html,
        IEnumerable<IReTourPlanParameterDto> parameters)
    {
        var any = false;
        foreach (var parameter in parameters)
        {
            any = true;
            html.Append("<span class=\"param\"><span>")
                .Append(E(parameter.DisplayName)).Append("</span><b>")
                .Append(E(parameter.Value)).Append("</b></span>");
        }
        if (!any)
        {
            html.Append("<span class=\"param\"><span>记录设置</span><b>未记录</b></span>");
        }
    }

    private static void AppendMetrics(
        StringBuilder html,
        IEnumerable<IReTourMetricDto> metrics)
    {
        var any = false;
        foreach (var metric in metrics)
        {
            any = true;
            html.Append("<span class=\"param\"><span>")
                .Append(E(metric.DisplayName)).Append("</span><b>")
                .Append(E(FormatMetric(metric))).Append("</b></span>");
        }
        if (!any)
        {
            html.Append("<span class=\"param\"><span>实际结果</span><b>未记录</b></span>");
        }
    }

    private static string FormatMetric(IReTourMetricDto metric)
    {
        if (!metric.NormalizedValue.HasValue)
        {
            return "未记录";
        }
        return IReTourReportMetricFormatter.Value(
            metric.MetricCode, metric.NormalizedValue.Value, metric.Unit);
    }

    private static string ActivityClass(string code) => code switch
    {
        "reaction_primary" or "reaction_secondary" => "game",
        "balance_bridge" => "balance",
        _ => string.Empty
    };

    private static string TrainingStateLabel(string state) => state switch
    {
        "completed" => "完整完成",
        "interrupted" => "训练中断",
        "not_started" => "尚未开始",
        "not_completed" => "未完成",
        _ => "状态待确认"
    };

    private static void HeroFact(StringBuilder html, string label, string value) =>
        html.Append("<div><span>").Append(E(label)).Append("</span><strong>")
            .Append(E(value)).Append("</strong></div>");

    private static void Count(StringBuilder html, string label, int value) =>
        html.Append("<div class=\"count-box\"><span>").Append(E(label))
            .Append("</span><strong>").Append(value).Append("</strong></div>");

    private static string SectionHead(string index, string title) =>
        $"<section class=\"section\"><div class=\"section-head\"><div><div class=\"section-index\">{E(index)}</div><h2>{E(title)}</h2></div></div>";

    private static string FormatTime(DateTimeOffset? value) =>
        value?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? "未记录";

    private static string E(string? value) =>
        WebUtility.HtmlEncode(value ?? string.Empty);
}
