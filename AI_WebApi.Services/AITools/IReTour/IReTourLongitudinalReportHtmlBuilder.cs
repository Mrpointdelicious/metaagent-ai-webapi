using System.Globalization;
using System.Net;
using System.Text;
using AI_WebApi.IServices.AITools;
using AI_WebApi.Model.DTOs.AITools;
using AI_WebApi.Services.AITools.IReGo;

namespace AI_WebApi.Services.AITools.IReTour;

/// <summary>
/// IReTour longitudinal presentation using the exact IReGo integrated-report
/// visual system and the same date-position/missing-point chart convention.
/// </summary>
public sealed class IReTourLongitudinalReportHtmlBuilder :
    IIReTourLongitudinalReportHtmlBuilder
{
    private const string ReportUsage =
        "本报告按日期汇总同类 IReTour 训练情况。同一指标有三个及以上有效记录方可展示；缺失日期保留位置，可见记录按时间顺序连接。报告仅描述数值变化，不等同于康复效果或病情改善。";

    public string Build(IReTourLongitudinalAnalysisDto analysis)
    {
        var html = new StringBuilder();
        html.Append("<!doctype html><html lang=\"zh-CN\"><head>")
            .Append("<meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">")
            .Append("<title>IRETOUR 统合汇报</title><style>")
            .Append(IReGoLongitudinalReportHtmlBuilder.VisualStyles)
            .Append("</style></head><body><article class=\"report\">");
        AppendHeader(html, analysis);
        html.Append("<main>");
        AppendTimeline(html, analysis);
        AppendMetrics(html, analysis);
        IReTourReadingHtml.Append(html, analysis.Interpretation, analysis.SourceSnapshot);
        html.Append("</main><section class=\"notice\"><strong>报告使用说明</strong>")
            .Append(ReportUsage)
            .Append("</section><footer><span>IRETOUR 统合汇报</span><span>数据来源：IReTour 训练汇总记录</span></footer>")
            .Append("</article></body></html>");
        return AI_WebApi.Services.AITools.PatientContext.RecordTenantPresentation.AddToHtml(html.ToString(), analysis);
    }

    private static void AppendHeader(
        StringBuilder html,
        IReTourLongitudinalAnalysisDto analysis)
    {
        var start = FormatDate(analysis.Window.StartedAt);
        var end = FormatDate(analysis.Window.EndedAt);
        var range = start == end ? start : $"{start}—{end}";
        var activity = Activity(analysis);
        html.Append("<header class=\"hero\"><div class=\"eyebrow\">IRETOUR · INTEGRATED REPORT</div>")
            .Append("<h1>统合汇报</h1><p>所选训练范围 · 只比较同一训练项目的同定义指标</p>")
            .Append("<div class=\"facts\">")
            .Append(Fact("训练范围", range))
            .Append(Fact("训练记录", $"{analysis.Window.ResolvedCount} 份"))
            .Append(Fact("可比指标", $"{analysis.MetricSeries.Count(Renderable)} 项"))
            .Append(Fact("训练项目", activity.DisplayName))
            .Append("</div></header>");
    }

    private static void AppendTimeline(
        StringBuilder html,
        IReTourLongitudinalAnalysisDto analysis)
    {
        html.Append(SectionHead(
                "01 · OVERVIEW",
                "总览",
                "按日期展示所选同类训练记录和结果可用情况。"))
            .Append("<div class=\"timeline\">");
        foreach (var session in analysis.Sessions)
        {
            var available = session.TrainingState == "completed" &&
                            session.ResultState == "summary_available" && session.ExclusionReasonCodes.Count == 0;
            html.Append("<div class=\"visit ")
                .Append(available ? "complete" : "incomplete")
                .Append("\"><span class=\"visit-number\">")
                .Append(session.Ordinal).Append("</span><div><b>")
                .Append(E(FormatTime(session.SessionTime))).Append("</b><p>")
                .Append(E(available ? "汇总结果可用于同指标比较" : "本次记录不参与指标比较"))
                .Append("</p></div><em>")
                .Append(available ? "完整" : "不完整")
                .Append("</em></div>");
        }
        html.Append("</div>");
        AppendDataSummary(html, analysis);
        html.Append("</section>");
    }

    private static void AppendDataSummary(
        StringBuilder html,
        IReTourLongitudinalAnalysisDto analysis)
    {
        var series = analysis.MetricSeries.Where(Renderable).ToArray();
        var increased = series.Count(x => x.Direction == "increased");
        var decreased = series.Count(x => x.Direction == "decreased");
        var unchanged = series.Count(x => x.Direction == "unchanged");
        var fluctuating = series.Count(x => x.Direction == "fluctuating");
        var title = series.Length == 0
            ? "训练记录还不够，暂时无法展示数值变化"
            : $"共 {series.Length} 组指标具有可比数值记录";
        var summary = series.Length == 0
            ? "需要同一指标至少三条有效记录。"
            : $"数值上升 {increased} 项、下降 {decreased} 项、持平 {unchanged} 项、波动 {fluctuating} 项。";

        html.Append("<div class=\"smart-eval overview-eval\"><div class=\"evaluation-heading\">")
            .Append("<h3>数据概览</h3><span class=\"ai-rule-badge\">后端规则汇总</span>")
            .Append("</div><div class=\"overview-summary\"><span class=\"status-badge ")
            .Append(series.Length == 0 ? "insufficient" : "stable")
            .Append("\">").Append(E(title)).Append("</span><div><strong>")
            .Append(E(summary)).Append("</strong><p>")
            .Append("上升、下降、持平和波动仅描述记录数值方向；不同指标的临床方向可能不同，不能据此判断康复效果。")
            .Append("</p></div></div><div class=\"evaluation-boundary\">")
            .Append("本报告不生成疗效或诊断结论，具体意义请由医生或治疗师结合康复评估判断。")
            .Append("</div></div>");
    }

    private static void AppendMetrics(
        StringBuilder html,
        IReTourLongitudinalAnalysisDto analysis)
    {
        var series = analysis.MetricSeries.Where(Renderable).ToArray();
        if (series.Length == 0)
        {
            return;
        }

        var activity = Activity(analysis);
        html.Append(SectionHead(
                "02 · SAME PROJECT",
                activity.DisplayName,
                "同训练项目 · 同定义指标按日期比较"))
            .Append("<article class=\"module\"><div class=\"module-head ")
            .Append(ProjectClass(activity.CanonicalCode))
            .Append("\"><div><small>同项目训练</small><h3>")
            .Append(E(activity.DisplayName)).Append("</h3></div><span>")
            .Append(series.Length).Append(" 项可比指标</span></div>")
            .Append("<div class=\"metric-list\">");
        var positions = analysis.Sessions.Select(session => new IReGoLongitudinalReportPositionDto
        {
            Ordinal = session.Ordinal,
            RecordTenant = session.RecordTenant,
            SessionTime = session.SessionTime
        }).ToArray();
        foreach (var metric in series)
        {
            // Adapt only the data; IReGo owns every chart element and its layout.
            var chartSeries = new IReGoLongitudinalMetricSeriesDto
            {
                MetricCode = metric.MetricCode,
                DisplayName = metric.DisplayName,
                Unit = IReTourReportMetricFormatter.Unit(metric.MetricCode, metric.Unit),
                Points = metric.Points.Select(point => new IReGoLongitudinalMetricPointDto
                {
                    ReportOrdinal = point.Ordinal,
                    RecordTenant = point.RecordTenant,
                    Status = point.ValueStatus,
                    NormalizedValue = IReTourReportMetricFormatter.IsRate(metric.MetricCode)
                        ? point.Value * 100m
                        : point.Value,
                    DisplayValue = point.Value.HasValue
                        ? IReTourReportMetricFormatter.Number(metric.MetricCode, point.Value.Value)
                        : null
                }).ToArray()
            };
            IReGoLongitudinalReportHtmlBuilder.AppendMetricChart(html, chartSeries, positions);
        }
        html.Append("</div></article></section>");
    }

    private static bool Renderable(IReTourMetricSeriesDto series) =>
        series.ComparisonStatus == "descriptive_same_source_field" &&
        series.Points.Count(x =>
            x.ValueStatus == "valid" && x.Value.HasValue) >= 3;

    private static (string CanonicalCode, string DisplayName) Activity(
        IReTourLongitudinalAnalysisDto analysis)
    {
        var code = analysis.Sessions.FirstOrDefault()?.CanonicalCode ?? "unknown_item";
        var item = IReTourItemRegistry.All()
            .FirstOrDefault(x => x.CanonicalCode == code);
        return (code, item?.DisplayName ?? "IReTour 训练");
    }

    private static string ProjectClass(string code) => code switch
    {
        "reaction_primary" or "reaction_secondary" => "game",
        "balance_bridge" => "balance",
        "straight_primary" or "straight_secondary" or "transverse" or
            "stride" or "sideway" or "gait_assessment" => "walking",
        _ => "other"
    };

    private static string SectionHead(string eyebrow, string title, string note) =>
        $"<section class=\"section\"><div class=\"head\"><div><small>{E(eyebrow)}</small><h2>{E(title)}</h2></div><p>{E(note)}</p></div>";

    private static string Fact(string label, string value) =>
        $"<div class=\"fact\"><span>{E(label)}</span><strong>{E(value)}</strong></div>";

    private static string FormatDate(DateTimeOffset? value) =>
        value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "时间缺失";

    private static string FormatTime(DateTimeOffset? value) =>
        value?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? "时间缺失";

    private static string E(string? value) =>
        WebUtility.HtmlEncode(value ?? string.Empty);
}
