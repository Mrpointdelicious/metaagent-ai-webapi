using System.Globalization;
using AI_WebApi.Model.DTOs.AITools;

namespace AI_WebApi.Services.AITools.IReTour;

/// <summary>Descriptive, traceable readings. No unvalidated clinical thresholds or model calls.</summary>
public static class IReTourReadingBuilder
{
    private static readonly string[] Boundaries =
    {
        "数值方向不等于临床改善或恶化；本工具不判定疗效或给出诊断。",
        "单位未确认的字段仅保留源数值，不换算、不套用其他设备的参考值。",
        "设置来自结果汇总，时长来自训练记录；尚未核验为处方计划，不据此计算计划完成率。"
    };

    public static IReTourReadingDto Single(IReTourSessionAnalysisDto data)
    {
        var valid = data.Result.Metrics.Where(x => x.ValueStatus == "valid" && x.NormalizedValue.HasValue).ToArray();
        return new()
        {
            Summary = $"本次{data.Activity.DisplayName}包含 {valid.Length} 项有效数值；训练状态为 {State(data.Time.TrainingState)}。",
            Observations = valid.Select(x => $"{x.DisplayName}：{x.NormalizedValue!.Value.ToString(CultureInfo.InvariantCulture)}{(x.Unit == null ? "（源单位未确认）" : " " + x.Unit)}；来源 {x.SourceField}。").ToArray(),
            Limitations = Boundaries.Concat(data.Quality.Issues.Select(x => x.PatientMessage ?? x.Code)).Distinct().ToArray(),
            NextChecks = new[] { "核对训练项目、时间和数据质量；需要纵向比较时选择同项目、同字段与相同设置的记录。" }
        };
    }

    public static IReTourReadingDto Trend(IReTourLongitudinalAnalysisDto data) => new()
    {
        Summary = $"所选范围包含 {data.Sessions.Count} 次记录，{data.Window.ExcludedSessionCount} 次未进入数值比较；有 {data.MetricSeries.Count(x => x.ComparisonStatus == "descriptive_same_source_field")} 组可描述的同口径指标。",
        Observations = data.MetricSeries.Where(x => x.ComparisonStatus == "descriptive_same_source_field").Select(x =>
            $"{x.DisplayName}（{x.SourceField}）：{x.Points.Count(p => p.Value.HasValue)} 个有效点，首末值 {x.FirstValue?.ToString(CultureInfo.InvariantCulture)} → {x.LastValue?.ToString(CultureInfo.InvariantCulture)}，数值{x.Direction switch { "increased" => "上升", "decreased" => "下降", "unchanged" => "不变", "fluctuating" => "波动", _ => "方向未确认" }}。").ToArray(),
        Limitations = Boundaries.Concat(new[] { "缺失与无效记录保留位置；字段别名或可见设置不同的记录分组，不合并成一条趋势。", "可见设置相同不代表所有训练负荷相同；设备未提供的设置仍未知。" }).ToArray(),
        NextChecks = new[] { "结合完整计划核对训练负荷和辅助设置；有缺口时先补核原始记录。" }
    };

    private static string State(string value) => value switch
    {
        "completed" => "已完成", "not_started" => "未开始", "not_completed" => "未完成",
        "interrupted" => "中断", _ => "未确认"
    };
}
