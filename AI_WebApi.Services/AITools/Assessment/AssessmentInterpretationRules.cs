using AI_WebApi.Model.DTOs.AITools;

namespace AI_WebApi.Services.AITools.Assessment;

/// <summary>
/// Per-scale reading rules. A rule may only describe a band when the band is backed by a
/// traceable source (product-owned report text or the published instrument standard);
/// everything else stays "unverified" and produces no verdict at all.
/// </summary>
public static class AssessmentInterpretationRules
{
    private sealed record Band(decimal Min, decimal Max, string Label, string Text);

    private sealed record Standard(
        decimal MaxScore,
        string EvidenceStatus,
        string EvidenceSource,
        IReadOnlyList<Band> Bands,
        IReadOnlyList<string> Basis,
        IReadOnlyList<string> Cautions,
        string? BlockedReason = null,
        string? BlockedSummary = null);

    private static readonly IReadOnlyDictionary<int, Standard> Standards =
        new Dictionary<int, Standard>
        {
            // AD8: the product itself ships the ">=2" note inside ScoreExplain.
            [1] = new Standard(8m, "source_reported", "dbreport.ReportDetails.ScoreExplain", new[]
            {
                new Band(2m, 8m, "达到源报告关注线", "本次得分 {score} 分，达到源报告提示的关注线（≥2 分）。"),
                new Band(0m, 1.99m, "未达到源报告关注线", "本次得分 {score} 分，未达到源报告提示的关注线（≥2 分）。")
            }, new[]
            {
                "AD8 共 8 题，每题选项分值 1 / 0 / 0.5，满分 8 分。",
                "源报告内置说明原文：「得分≥2 有可能为早期失智表现，建议进行专业诊断和评估。」"
            }, new[]
            {
                "AD8 是自评筛查工具，结果为提示性信息，不等同于诊断。",
                "关注线来自源报告文案，尚未经临床签核。"
            }),

            // Barthel: the product report renders "得分说明：日常生活独立" for 100 points;
            // the remaining bands follow the published Barthel grading.
            [2] = new Standard(100m, "standard_reference", "Barthel 指数通用分级 + 产品报告样例", new[]
            {
                new Band(100m, 100m, "独立", "本次得分 {score} 分，对应「日常生活独立」。"),
                new Band(61m, 99.99m, "轻度依赖", "本次得分 {score} 分，对应「轻度依赖」。"),
                new Band(41m, 60.99m, "中度依赖", "本次得分 {score} 分，对应「中度依赖」。"),
                new Band(21m, 40.99m, "重度依赖", "本次得分 {score} 分，对应「重度依赖」。"),
                new Band(0m, 20.99m, "完全依赖", "本次得分 {score} 分，对应「完全依赖」。")
            }, new[]
            {
                "Barthel 指数满分 100 分，共 10 个条目。",
                "产品报告样例：100 分对应「得分说明：日常生活独立」。",
                "通用分级：100 独立；61–99 轻度依赖；41–60 中度依赖；21–40 重度依赖；≤20 完全依赖。"
            }, new[]
            {
                "分级用于描述当下的功能状态，不代表康复效果或病情变化。",
                "通用分级尚未与产品实际情况逐条核对。"
            }),

            // GAD-7: published bands only; no product copy was provided.
            [5] = new Standard(21m, "standard_reference", "GAD-7 通用分级", new[]
            {
                new Band(15m, 21m, "重度", "本次得分 {score} 分，落在「重度」区间（15–21）。"),
                new Band(10m, 14.99m, "中度", "本次得分 {score} 分，落在「中度」区间（10–14）。"),
                new Band(5m, 9.99m, "轻度", "本次得分 {score} 分，落在「轻度」区间（5–9）。"),
                new Band(0m, 4.99m, "无或最低", "本次得分 {score} 分，落在「无或最低」区间（0–4）。")
            }, new[]
            {
                "GAD-7 共 7 题，满分 21 分。",
                "通用分级：0–4 无或最低；5–9 轻度；10–14 中度；15–21 重度。"
            }, new[]
            {
                "量表询问的是最近两周的感受，受近期状态影响。",
                "分级尚未经临床签核，仅作描述。"
            }),

            // MoCA-B: 30-point instrument whose cut-off depends on years of education, and the
            // platform does not store education. Refuse to grade instead of guessing.
            [11] = new Standard(30m, "blocked_requires_education", "MoCA-B 判读依赖受教育年限", Array.Empty<Band>(),
                new[]
                {
                    "MoCA-B 满分 30 分，报告按维度给出得分。",
                    "判读需按受教育年限选择对应分界值，而系统未登记患者的受教育年限。"
                }, new[]
                {
                    "缺少受教育年限，无法给出任何分界判断。",
                    "只呈现总分与维度分布，不做正常 / 异常的结论。"
                },
                BlockedReason: "education_years_missing",
                BlockedSummary: "本次得分 {score} 分（满分 30）。MoCA-B 的判读需要结合受教育年限，系统内未登记该信息，因此只呈现分数，不做判读。")
        };

    /// <summary>Returns null when no traceable rule exists; never invents a verdict.</summary>
    public static AssessmentInterpretationDto? Interpret(int? scaleType, decimal? score, decimal? maxScore)
    {
        if (scaleType is null || score is null) return null;
        if (!Standards.TryGetValue(scaleType.Value, out var standard)) return null;
        var text = Format(score.Value);
        if (standard.BlockedReason is not null)
            return new AssessmentInterpretationDto
            {
                Level = "unverified",
                BandLabel = "未判读",
                Summary = (standard.BlockedSummary ?? "缺少判读所需信息，未做结论。").Replace("{score}", text),
                Basis = standard.Basis,
                Cautions = standard.Cautions,
                EvidenceStatus = standard.EvidenceStatus,
                EvidenceSource = standard.EvidenceSource
            };

        var band = standard.Bands.FirstOrDefault(x => score.Value >= x.Min && score.Value <= x.Max);
        if (band is null) return null;
        return new AssessmentInterpretationDto
        {
            Level = "descriptive",
            BandLabel = band.Label,
            Summary = band.Text.Replace("{score}", text),
            Basis = standard.Basis,
            Cautions = standard.Cautions,
            EvidenceStatus = standard.EvidenceStatus,
            EvidenceSource = standard.EvidenceSource
        };
    }

    /// <summary>Full standard for one scale, used by the report chart and by catalogue queries.</summary>
    public static AssessmentStandardDto? Describe(int scaleType) =>
        !Standards.TryGetValue(scaleType, out var standard)
            ? null
            : new AssessmentStandardDto
            {
                ScaleType = scaleType,
                MaxScore = standard.MaxScore,
                EvidenceStatus = standard.EvidenceStatus,
                EvidenceSource = standard.EvidenceSource,
                Bands = standard.Bands.Select(x => new AssessmentBandDto
                {
                    Min = x.Min, Max = x.Max, Label = x.Label,
                    Text = x.Text.Replace("{score}", "X")
                }).ToArray(),
                Basis = standard.Basis,
                Cautions = standard.Cautions,
                IsBlocked = standard.BlockedReason is not null
            };

    private static string Format(decimal value) =>
        value == decimal.Truncate(value)
            ? decimal.Truncate(value).ToString("0", System.Globalization.CultureInfo.InvariantCulture)
            : value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
}
