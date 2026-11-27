namespace AI_WebApi.Services.AITools.Assessment;

/// <summary>
/// Patient-safe Chinese wording for assessment reason codes. The codes stay stable for
/// machines; the notes are what an agent can read out loud without leaking internals
/// and without turning a data gap into a clinical statement.
/// </summary>
public static class AssessmentReasonText
{
    private const string Fallback = "该记录存在尚未确认的数据问题，暂不建议据此下结论。";

    private static readonly IReadOnlyDictionary<string, string> Notes =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // Tool level
            ["ASSESSMENT_INVALID_REQUEST"] = "查询参数不符合量表工具的约定。",
            ["ASSESSMENT_SIGNING_KEY_NOT_CONFIGURED"] = "服务端未配置量表记录签名，暂无法提供记录。",
            ["ASSESSMENT_PATIENT_SCOPE_UNAVAILABLE"] = "无法确认当前患者的数据范围。",
            ["ASSESSMENT_REF_NOT_FOUND"] = "没有符合条件的量表记录。",
            ["ASSESSMENT_INVALID_WINDOW"] = "所选记录条数不在允许范围内。",
            ["ASSESSMENT_READ_LIMIT_EXCEEDED"] = "记录数量超出当前查询上限。",
            ["ASSESSMENT_DEPENDENCY_UNAVAILABLE"] = "量表数据源暂不可用。",
            ["ASSESSMENT_ANALYSIS_REQUIRED"] = "需要先分析记录，再生成报表。",
            ["ASSESSMENT_SOURCE_CHANGED"] = "数据已变化，请重新分析后再生成报表。",
            ["ASSESSMENT_REPORT_STORE_NOT_CONFIGURED"] = "报表存储尚未配置。",
            ["ASSESSMENT_RENDERING_UNAVAILABLE"] = "报表渲染暂不可用。",

            // Plan side
            ["ASSESSMENT_PLAN_SCHEMA_UNSUPPORTED"] = "这条计划的内部结构无法解析，已跳过。",
            ["ASSESSMENT_PLAN_NAME_MISSING"] = "这条计划没有填写量表名称。",
            ["ASSESSMENT_SCALE_UNIDENTIFIED"] = "无法识别这是哪一个量表。",
            ["ASSESSMENT_SCALE_FROM_PLAN_NAME"] = "计划里没有可用类型，量表身份由计划名称确定。",
            ["ASSESSMENT_SCALE_FROM_REPORT_TITLE"] = "计划里没有可用类型，量表身份由报告标题确定。",
            ["ASSESSMENT_PLAN_CATALOG_CONFLICT"] = "计划里的量表名称与类型不一致，无法确认身份。",

            // Result side
            ["ASSESSMENT_RESULT_MISSING"] = "只有计划、没有结果报告；不能当作 0 分，也不能当作未完成。",
            ["ASSESSMENT_RESULT_PARTIALLY_MISSING"] = "关联报告中有部分为空，已按剩余报告解析。",
            ["ASSESSMENT_REPORT_CONFLICT"] = "同一计划下的报告内容互相不一致，暂不展示分数。",
            ["ASSESSMENT_REPORT_IDENTITY_UNVERIFIED"] = "计划记录的量表与报告标题对不上，无法确认是哪一份量表，因此不展示分数。",
            ["ASSESSMENT_INVALID_SCORE"] = "报告里的总分不是可解析的数值。",
            ["ASSESSMENT_REPORT_SCHEMA_UNSUPPORTED"] = "报告结构与当前支持的格式都不匹配，暂不能解析。",

            // Score semantics
            ["ASSESSMENT_SCORE_SEMANTICS_UNVERIFIED"] = "总分直接取自源报告，计分规则与分界值尚未核验；只能报数字，不能判断高低或正常异常。",
            ["ASSESSMENT_SCORE_RECOMPUTED"] = "已按每题选项分值重算总分，与源报告一致。",
            ["ASSESSMENT_SCORE_MISMATCH"] = "源报告总分与按选项重算的结果不一致，需要核对原始记录。",
            ["ASSESSMENT_SCORING_RULES_UNVERIFIED"] = "多次记录之间的计分规则尚未核验，暂不做跨次分数比较。"
        };

    public static string Describe(string? code) =>
        code is not null && Notes.TryGetValue(code, out var note) ? note : Fallback;

    public static IReadOnlyList<string> DescribeAll(IEnumerable<string>? codes) =>
        codes is null
            ? Array.Empty<string>()
            : codes.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal).Select(Describe).ToArray();
}
