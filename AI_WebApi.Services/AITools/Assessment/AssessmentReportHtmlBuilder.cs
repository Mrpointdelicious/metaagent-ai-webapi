using System.Globalization;
using System.Net;
using System.Text;
using AI_WebApi.Model.DTOs.AITools;

namespace AI_WebApi.Services.AITools.Assessment;

/// <summary>
/// Renders the assessment report as a single self-contained HTML document.
/// Everything is inline CSS / SVG / conic-gradient: the PNG renderer loads the page with
/// no network access, so external chart libraries would render an empty canvas.
/// </summary>
public static class AssessmentReportHtmlBuilder
{
    private static readonly string[] Palette = { "#249b91", "#2789b5", "#7669c8", "#f2a51f", "#db6d77", "#55a968" };

    public static string Build(AssessmentToolDataDto data)
    {
        var primary = data.Records.FirstOrDefault();
        var html = new StringBuilder(16 * 1024);
        html.Append("<!doctype html><html lang=\"zh-CN\"><head><meta charset=\"utf-8\">");
        html.Append("<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
        html.Append("<style>").Append(Styles).Append("</style></head><body><article class=\"report\">");
        BuildHero(html, data, primary);
        html.Append("<main>");
        BuildOverview(html, primary);
        BuildStandard(html, primary);
        BuildItems(html, data, primary);
        BuildDisclaimer(html, data);
        html.Append("</main>");
        BuildFooter(html, primary);
        html.Append("</article></body></html>");
        return html.ToString();
    }

    private static void BuildHero(StringBuilder html, AssessmentToolDataDto data, AssessmentRecordDto? primary)
    {
        html.Append("<header class=\"hero\"><div class=\"eyebrow\">ONLINE ASSESSMENT · SINGLE SCALE REPORT</div>");
        html.Append("<h1>量表测评报告</h1><div class=\"hero-sub\">本次线上量表作答与源报告分数</div>");
        html.Append("<div class=\"hero-meta\">");
        Fact(html, "记录时间", Time(primary));
        Fact(html, "量表", primary?.ScaleName ?? "待识别量表");
        Fact(html, "评分状态", ScoringLabel(primary));
        Fact(html, "题目数", primary is { QuestionCount: > 0 } ? $"{primary.QuestionCount} 题" : "未记录");
        html.Append("</div></header>");
    }

    private static void BuildOverview(StringBuilder html, AssessmentRecordDto? record)
    {
        html.Append(SectionHead("01 · 测评总览", "测评总览"));
        html.Append("<div class=\"overview\">");

        html.Append("<div class=\"panel\"><div class=\"panel-title\">本次得分</div>");
        if (record?.ReportedScore is { } score)
        {
            html.Append("<div class=\"score-row\"><strong>").Append(Num(score)).Append("</strong>");
            if (record.ScaleMaxScore is { } max) html.Append("<span>/ ").Append(Num(max)).Append(" 分</span>");
            else html.Append("<span>分</span>");
            html.Append("</div>");
            html.Append("<div class=\"badges\">");
            html.Append("<span class=\"badge ").Append(record.ScoringStatus == "verified_option_sum" ? "ok" : "warn").Append("\">")
                .Append(E(ScoringBadge(record))).Append("</span>");
            if (record.RecomputedScore is { } recomputed)
                html.Append("<span class=\"badge ").Append(recomputed == score ? "ok" : "danger").Append("\">")
                    .Append(recomputed == score ? "复算一致" : "复算不一致").Append("</span>");
            html.Append("</div>");
        }
        else
        {
            html.Append("<div class=\"score-row\"><strong>—</strong><span>缺少可核验总分</span></div>");
            html.Append("<div class=\"badges\"><span class=\"badge danger\">").Append(E(StateLabel(record))).Append("</span></div>");
        }

        if (record is { QuestionCount: > 0 })
        {
            var answered = record.AnsweredItemCount;
            var rate = answered is { } a ? Math.Clamp((double)a / record.QuestionCount * 100, 0, 100) : 0;
            html.Append("<div class=\"completion-row\"><span>作答完成度</span><strong>")
                .Append(answered is { } done ? $"{done} / {record.QuestionCount} 题" : "未记录逐题作答")
                .Append("</strong></div><div class=\"bar\"><i style=\"width:")
                .Append(rate.ToString("0.##", CultureInfo.InvariantCulture)).Append("%\"></i></div>");
        }

        if (record?.Interpretation is { } reading)
        {
            html.Append("<div class=\"reading\"><strong>").Append(E(reading.BandLabel)).Append("</strong><p>")
                .Append(E(reading.Summary)).Append("</p></div>");
        }

        html.Append("</div>");
        BuildRatePanel(html, record);
        html.Append("</div></section>");
    }

    private static void BuildRatePanel(StringBuilder html, AssessmentRecordDto? record)
    {
        html.Append("<div class=\"panel\"><div class=\"panel-title\">得分占比</div>");
        if (record?.ReportedScore is not { } score || record.ScaleMaxScore is not { } max || max <= 0)
        {
            html.Append("<div class=\"empty\">该量表尚未确认满分，暂不计算得分率</div></div>");
            return;
        }

        var percent = Math.Clamp((double)(score / max) * 100, 0, 100);
        html.Append("<div class=\"donut-wrap\"><div class=\"donut\" style=\"background:conic-gradient(")
            .Append(Palette[0]).Append(' ').Append(percent.ToString("0.##", CultureInfo.InvariantCulture)).Append("%, #e3ecef ")
            .Append(percent.ToString("0.##", CultureInfo.InvariantCulture)).Append("% 100%)\"><div><strong>")
            .Append(percent.ToString("0.#", CultureInfo.InvariantCulture)).Append("%</strong><span>得分率</span></div></div>");
        html.Append("<div class=\"legend\"><span><i style=\"background:").Append(Palette[0]).Append("\"></i>已得 ")
            .Append(Num(score)).Append(" 分</span><span><i style=\"background:#e3ecef\"></i>未得 ")
            .Append(Num(max - score)).Append(" 分</span></div></div></div>");
    }

    private static void BuildStandard(StringBuilder html, AssessmentRecordDto? record)
    {
        html.Append(SectionHead("02 · 评分标准与解读", "评分标准与解读"));
        html.Append("<div class=\"panel wide\">");
        if (record?.ScaleType is not { } type || AssessmentInterpretationRules.Describe(type) is not { } standard)
        {
            html.Append("<div class=\"empty\">该量表尚未建立可追溯的评判标准，仅呈现分数，不做解读。</div></div></section>");
            return;
        }

        html.Append("<div class=\"standard-head\"><span>满分 ").Append(Num(standard.MaxScore)).Append(" 分</span>")
            .Append("<span class=\"badge ").Append(standard.IsBlocked ? "warn" : "ok").Append("\">")
            .Append(E(EvidenceLabel(standard.EvidenceStatus))).Append("</span></div>");

        if (standard.IsBlocked)
            html.Append("<div class=\"empty\">依据不足，未做判读（原因见 01 区）。</div>");
        else
        {
            html.Append("<table class=\"bands\"><tr><th>区间</th><th>分级</th><th>说明</th></tr>");
            foreach (var band in standard.Bands)
            {
                var active = record.ReportedScore is { } score && score >= band.Min && score <= band.Max;
                html.Append("<tr class=\"").Append(active ? "active" : string.Empty).Append("\"><td>")
                    .Append(Num(band.Min)).Append(" – ").Append(Num(band.Max)).Append("</td><td>")
                    .Append(E(band.Label)).Append("</td><td>").Append(E(band.Text)).Append("</td></tr>");
            }
            html.Append("</table>");
        }

        html.Append("<div class=\"basis\"><div class=\"basis-title\">解读依据</div><ul>");
        foreach (var line in standard.Basis) html.Append("<li>").Append(E(line)).Append("</li>");
        html.Append("</ul></div>");
        html.Append("<div class=\"basis\"><div class=\"basis-title\">使用限制</div><ul>");
        foreach (var line in standard.Cautions) html.Append("<li>").Append(E(line)).Append("</li>");
        html.Append("</ul></div>");
        html.Append("</div></section>");
    }

    private static void BuildItems(StringBuilder html, AssessmentToolDataDto data, AssessmentRecordDto? record)
    {
        html.Append(SectionHead("03 · 逐题作答", "逐题作答"));
        html.Append("<div class=\"panel wide\">");
        var items = record?.Items ?? Array.Empty<AssessmentItemDto>();
        if (items.Count == 0)
        {
            html.Append("<div class=\"empty\">").Append(record is { QuestionCount: > 0 }
                ? $"该量表本次记录了总分，但未记录逐题作答（共 {record.QuestionCount} 题）。"
                : "该记录没有逐题作答数据。").Append("</div>");
        }
        else
        {
            html.Append("<div class=\"chart-head\"><span>每题得分</span><span>共 ").Append(items.Count)
                .Append(" 题 · 已答 ").Append(items.Count(x => x.Answered)).Append(" 题</span></div>");
            AppendBarChart(html, items);
            html.Append("<div class=\"chart-legend\">");
            if (items.Any(x => x.MaxScore is > 0))
                html.Append("<span><i style=\"background:").Append(Palette[0]).Append("\"></i>满分</span>")
                    .Append("<span><i style=\"background:").Append(Palette[3]).Append("\"></i>部分得分</span>")
                    .Append("<span><i style=\"background:#c9d6dc\"></i>0 分</span>")
                    .Append("<span><i style=\"background:#dde7eb\"></i>未作答</span>");
            else
                html.Append("<span><i style=\"background:").Append(Palette[0]).Append("\"></i>已作答</span>")
                    .Append("<span><i style=\"background:#dde7eb\"></i>未作答</span>")
                    .Append("<span style=\"color:#7d939d\">该量表未提供每题满分，按「作答 / 未作答」展示</span>");
            html.Append("</div>");
        }

        if (data.Records.Count > 1) AppendRecordTable(html, data.Records);
        html.Append("</div></section>");
    }

    private static void AppendBarChart(StringBuilder html, IReadOnlyList<AssessmentItemDto> items)
    {
        const int width = 1040, height = 250, baseline = 205, top = 24;
        var gap = 4;
        var slot = Math.Max(12, (width - 60) / Math.Max(1, items.Count));
        var barWidth = Math.Max(8, slot - gap);
        html.Append("<svg class=\"bars\" viewBox=\"0 0 ").Append(width).Append(' ').Append(height)
            .Append("\" width=\"100%\" role=\"img\">");
        html.Append("<line x1=\"30\" y1=\"").Append(baseline).Append("\" x2=\"").Append(width - 30)
            .Append("\" y2=\"").Append(baseline).Append("\" stroke=\"#d5e1e6\" stroke-width=\"1\"/>");
        var knownMax = items.Any(x => x.MaxScore is > 0);
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var x = 30 + i * slot;
            if (!item.Answered)
            {
                html.Append("<rect x=\"").Append(x).Append("\" y=\"").Append(baseline - 8).Append("\" width=\"")
                    .Append(barWidth).Append("\" height=\"8\" rx=\"2\" fill=\"#dde7eb\"/>");
                continue;
            }

            int barHeight;
            string color;
            if (!knownMax)
            {
                // 没有每题满分时退化成「作答/未作答」二值图，避免画出一排看不见的柱子。
                barHeight = 120;
                color = Palette[0];
            }
            else
            {
                var max = item.MaxScore is > 0 ? item.MaxScore.Value : 1m;
                var ratio = Math.Clamp((double)(item.Score ?? 0m) / (double)max, 0, 1);
                barHeight = Math.Max(6, (int)Math.Round((baseline - top) * ratio));
                color = ratio >= 0.999 ? Palette[0] : ratio <= 0 ? "#c9d6dc" : Palette[3];
            }

            html.Append("<rect x=\"").Append(x).Append("\" y=\"").Append(baseline - barHeight).Append("\" width=\"")
                .Append(barWidth).Append("\" height=\"").Append(barHeight).Append("\" rx=\"3\" fill=\"").Append(color).Append("\"/>");
        }

        foreach (var index in new[] { 0, items.Count / 2, items.Count - 1 }.Distinct())
        {
            var x = 30 + index * slot + barWidth / 2;
            html.Append("<text x=\"").Append(x).Append("\" y=\"").Append(baseline + 22)
                .Append("\" font-size=\"15\" fill=\"#7d939d\" text-anchor=\"middle\">Q").Append(index + 1).Append("</text>");
        }
        html.Append("</svg>");
    }

    private static void AppendRecordTable(StringBuilder html, IReadOnlyList<AssessmentRecordDto> records)
    {
        html.Append("<table class=\"records\"><tr><th>记录时间</th><th>量表</th><th>源总分</th><th>分级</th><th>状态</th></tr>");
        foreach (var record in records)
        {
            html.Append("<tr><td>").Append(E(Time(record))).Append("</td><td>").Append(E(record.ScaleName ?? "待识别量表"))
                .Append("</td><td>").Append(E(record.ReportedScore is { } score ? Num(score) : "—"))
                .Append("</td><td>").Append(E(record.Interpretation?.BandLabel ?? "—"))
                .Append("</td><td>").Append(E(StateLabel(record))).Append("</td></tr>");
        }
        html.Append("</table>");
    }

    private static void BuildDisclaimer(StringBuilder html, AssessmentToolDataDto data)
    {
        html.Append("<section class=\"disclaimer\"><strong>报告使用说明</strong><p>")
            .Append("本报告根据线上量表记录自动生成，仅用于了解本次作答情况，不替代医生或治疗师的诊断与评估。")
            .Append("数值变化不等同于康复效果；题目与选项原文不对外展示。</p><ul>");
        foreach (var note in data.Interpretation.Limitations.Concat(data.Interpretation.NextChecks))
            html.Append("<li>").Append(E(note)).Append("</li>");
        html.Append("</ul></section>");
    }

    private static void BuildFooter(StringBuilder html, AssessmentRecordDto? record) =>
        html.Append("<footer><span>ONLINE ASSESSMENT 量表测评报告</span><span>")
            .Append(E(record?.RecordedAt?.ToString("yyyy-MM-dd") ?? string.Empty)).Append("</span></footer>");

    private static string SectionHead(string index, string title) =>
        $"<section class=\"section\"><div class=\"section-index\">{E(index)}</div><h2>{E(title)}</h2>";

    private static void Fact(StringBuilder html, string label, string value) =>
        html.Append("<div><span>").Append(E(label)).Append("</span><strong>").Append(E(value)).Append("</strong></div>");

    private static string Time(AssessmentRecordDto? record) =>
        record?.RecordedAt is { } at
            ? at.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
            : "未记录";

    private static string ScoringBadge(AssessmentRecordDto record) => record.ScoringStatus switch
    {
        "verified_option_sum" => "计分规则已核验",
        "unverified_education_adjusted" => "计分规则待核验",
        _ => "计分规则未核验"
    };

    private static string ScoringLabel(AssessmentRecordDto? record) => record is null
        ? "未知"
        : record.ReportedScore is null ? "缺少结果" : ScoringBadge(record);

    private static string StateLabel(AssessmentRecordDto? record) => record?.ResultState switch
    {
        "reported_total_unverified" => "有源总分",
        "no_result" => "缺少结果",
        "conflicting" => "报告冲突",
        "identity_conflict" => "身份待核对",
        "invalid_score" => "分数无效",
        "unsupported_schema" => "结构不支持",
        _ => "未知状态"
    };

    private static string EvidenceLabel(string status) => status switch
    {
        "source_reported" => "依据：源报告文案",
        "standard_reference" => "依据：量表通用分级",
        "blocked_requires_education" => "依据不足，未判读",
        _ => "依据未核验"
    };

    private static string Num(decimal value) => value == decimal.Truncate(value)
        ? decimal.Truncate(value).ToString("0", CultureInfo.InvariantCulture)
        : value.ToString("0.##", CultureInfo.InvariantCulture);

    private static string E(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);

    private const string Styles = @"
        *{box-sizing:border-box}
        body{margin:0;padding:32px;background:#eef3f5;color:#17313f;
             font:16px/1.65 ""Microsoft YaHei"",""PingFang SC"",""Noto Sans CJK SC"",sans-serif}
        .report{max-width:1120px;margin:0 auto;background:#fff;border-radius:22px;overflow:hidden;
                box-shadow:0 18px 48px rgba(16,49,63,.12)}
        .hero{padding:38px 44px 30px;color:#fff;
              background:linear-gradient(135deg,#0c3a4d 0%,#11616c 52%,#2aa198 100%)}
        .eyebrow{font-size:12px;letter-spacing:.28em;opacity:.78}
        .hero h1{margin:14px 0 6px;font-size:38px;letter-spacing:.04em}
        .hero-sub{font-size:15px;opacity:.85}
        .hero-meta{display:grid;grid-template-columns:repeat(4,1fr);gap:14px;margin-top:26px}
        .hero-meta div{background:rgba(255,255,255,.12);border:1px solid rgba(255,255,255,.18);
                       border-radius:12px;padding:12px 14px}
        .hero-meta span{display:block;font-size:12px;opacity:.8}
        .hero-meta strong{display:block;margin-top:4px;font-size:16px;font-weight:600}
        main{padding:34px 44px 8px}
        .section{margin-bottom:30px}
        .section-index{font-size:12px;letter-spacing:.2em;color:#7d939d}
        .section h2{margin:4px 0 16px;font-size:24px}
        .overview{display:grid;grid-template-columns:1.25fr 1fr;gap:18px}
        .panel{border:1px solid #dde7eb;border-radius:16px;padding:20px 22px;background:#fff}
        .panel.wide{width:100%}
        .panel-title{font-size:14px;color:#5d7885;margin-bottom:12px}
        .score-row{display:flex;align-items:baseline;gap:10px}
        .score-row strong{font-size:52px;line-height:1.1;color:#0f5f68}
        .score-row span{font-size:15px;color:#5d7885}
        .badges{display:flex;gap:8px;margin-top:10px;flex-wrap:wrap}
        .badge{font-size:12px;padding:4px 10px;border-radius:999px;background:#eef3f5;color:#41606d}
        .badge.ok{background:#e2f3f0;color:#12796f}
        .badge.warn{background:#fdf1dc;color:#a8701b}
        .badge.danger{background:#fbe5e7;color:#b03a45}
        .completion-row{display:flex;justify-content:space-between;margin:18px 0 8px;font-size:13px;color:#5d7885}
        .bar{height:10px;border-radius:999px;background:#e8eff2;overflow:hidden}
        .bar i{display:block;height:100%;background:linear-gradient(90deg,#249b91,#3fb6a4)}
        .reading{margin-top:16px;padding:14px 16px;border-left:4px solid #249b91;background:#f4faf9;border-radius:0 10px 10px 0}
        .reading strong{display:block;margin-bottom:4px;color:#11616c}
        .reading p{margin:0;font-size:14px;color:#33505e}
        .empty{padding:34px 18px;text-align:center;color:#7d939d;font-size:14px;border:1px dashed #d5e1e6;border-radius:12px}
        .donut-wrap{display:flex;align-items:center;gap:24px}
        .donut{width:168px;height:168px;border-radius:50%;display:grid;place-items:center;flex:none}
        .donut>div{width:118px;height:118px;border-radius:50%;background:#fff;display:grid;place-items:center;text-align:center}
        .donut strong{display:block;font-size:26px;color:#0f5f68}
        .donut span{font-size:11px;color:#7d939d}
        .legend{display:flex;flex-direction:column;gap:10px;font-size:13px;color:#41606d}
        .legend i{display:inline-block;width:10px;height:10px;border-radius:3px;margin-right:8px}
        .standard-head{display:flex;align-items:center;gap:12px;margin-bottom:14px;font-size:14px;color:#5d7885}
        table{width:100%;border-collapse:collapse;font-size:14px}
        .bands th,.records th{text-align:left;padding:10px 12px;background:#f2f7f8;color:#5d7885;font-weight:600}
        .bands td,.records td{padding:10px 12px;border-bottom:1px solid #e8eff2;vertical-align:top}
        .bands tr.active td{background:#eefaf7;font-weight:600;color:#0f5f68}
        .basis{margin-top:16px}
        .basis-title{font-size:13px;color:#7d939d;margin-bottom:6px}
        .basis ul{margin:0;padding-left:18px;font-size:13px;color:#41606d}
        .basis li{margin:3px 0}
        .chart-head{display:flex;justify-content:space-between;font-size:13px;color:#5d7885;margin-bottom:8px}
        .bars{display:block;width:100%;height:auto}
        .chart-legend{display:flex;gap:18px;margin-top:10px;font-size:12px;color:#5d7885;flex-wrap:wrap}
        .chart-legend i{display:inline-block;width:10px;height:10px;border-radius:3px;margin-right:6px}
        .records{margin-top:20px}
        .disclaimer{margin:26px 0 8px;padding:20px 22px;background:#f4f8f9;border-radius:14px;font-size:13px;color:#41606d}
        .disclaimer strong{display:block;margin-bottom:6px;color:#17313f}
        .disclaimer p{margin:0 0 8px}
        .disclaimer ul{margin:0;padding-left:18px}
        footer{display:flex;justify-content:space-between;padding:18px 44px;border-top:1px solid #e8eff2;
               font-size:12px;color:#7d939d}
        ";
}
