using System.Collections.ObjectModel;
using System.Text;

namespace AI_WebApi.Services.AITools.Assessment;

/// <summary>Product identity directory, not a clinical scoring/threshold registry.</summary>
public static class AssessmentScaleCatalog
{
    public const string Version = "project-online-scale-catalog-v2";

    public static IReadOnlySet<string> OnlineGames { get; } = new HashSet<string>(StringComparer.Ordinal)
    { "饥饿的小鸟", "色盲测试", "小镇警探", "打地鼠", "舒尔特方格", "沙滩坦克", "记忆速配" };

    private static readonly IReadOnlySet<string> NormalizedOnlineGames =
        new HashSet<string>(OnlineGames.Select(NormalizeName), StringComparer.Ordinal);

    public static IReadOnlyDictionary<int, string> Names { get; } = new ReadOnlyDictionary<int, string>(new Dictionary<int, string>
    {
        [1] = "AD8认知障碍自评表", [2] = "ADL量表-Barthel指数", [3] = "ADL日常生活能力量表",
        [4] = "Beck抑郁量表", [5] = "GAD-7广泛性焦虑量表", [6] = "Rivermead行为记忆量表记录表",
        [7] = "汉密尔顿抑郁量表", [8] = "简易认知状态评价量表", [9] = "简易智力状态评估表",
        [10] = "老年人认知功能筛查量表", [11] = "蒙特利尔认知评估量表记录表",
        [12] = "认知障碍分级评定表", [13] = "色盲色弱检测国际量表"
    });

    /// <summary>
    /// Naming variants observed in project.dbreport.ReportDetails. Only real observed spellings
    /// belong here; a variant aliases the same product identity, it is not a new scale.
    /// </summary>
    private static readonly IReadOnlyDictionary<int, IReadOnlyList<string>> Variants =
        new Dictionary<int, IReadOnlyList<string>>
        {
            // Legacy reports write "AD8" instead of the full catalogue title.
            [1] = new[] { "AD8" },
            // MoCA-B reports append the instrument acronym to the catalogue title.
            [11] = new[]
            {
                "蒙特利尔认知评估量表(MoCA-B)",
                "蒙特利尔认知评估量表MoCA-B",
                "MoCA-B",
                "MoCA"
            }
        };

    /// <summary>Per-scale facts that are safe to expose. Scoring rules stay unverified unless listed here.</summary>
    public static IReadOnlyDictionary<int, ScaleProfile> Profiles { get; } =
        new ReadOnlyDictionary<int, ScaleProfile>(new Dictionary<int, ScaleProfile>
        {
            // Option scores 1 / 0 / 0.5 for "是，有改变 / 不是，没有改变 / 不知道";
            // eight items, maximum 8. Verified against project.dbreport samples.
            [1] = new ScaleProfile(8, 8m, "verified_option_sum", "legacy_format_only"),
            // MoCA-B is a 30-point instrument with education adjustment; its source total
            // cannot be re-derived by summing option scores without the signed-off rule.
            [11] = new ScaleProfile(null, null, "unverified_education_adjusted", "recorded")
        });

    public static ScaleProfile ProfileFor(int type) =>
        Profiles.TryGetValue(type, out var profile) ? profile : ScaleProfile.Unknown;

    /// <summary>Strips whitespace and unifies full-width brackets, matching how the products write names.</summary>
    public static string NormalizeName(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (char.IsWhiteSpace(ch)) continue;
            builder.Append(ch switch
            {
                '（' => '(',
                '）' => ')',
                '－' => '-',
                _ => ch
            });
        }
        return builder.ToString();
    }

    public static bool IsOnlineGame(string? name)
    {
        var normalized = NormalizeName(name);
        return normalized.Length > 0 && NormalizedOnlineGames.Contains(normalized);
    }

    public static bool Matches(int type, string? name)
    {
        var normalized = NormalizeName(name);
        if (normalized.Length == 0) return false;
        if (Names.TryGetValue(type, out var canonical) && NormalizeName(canonical) == normalized) return true;
        return Variants.TryGetValue(type, out var variants) && variants.Any(x => NormalizeName(x) == normalized);
    }

    /// <summary>Resolves a catalogue type from a scale name, used when the plan carried no usable type.</summary>
    public static int? ResolveType(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        foreach (var pair in Names)
            if (Matches(pair.Key, name)) return pair.Key;
        return null;
    }
}

/// <summary>
/// Product-level facts for one scale. <see cref="ScoringStatus"/> is deliberately conservative:
/// only a rule verified against real samples may be used to re-derive a total.
/// </summary>
public sealed record ScaleProfile(int? ItemCount, decimal? MaxScore, string ScoringStatus, string ItemAnswerStatus)
{
    public static ScaleProfile Unknown { get; } = new(null, null, "unverified", "unknown");
}
