namespace AI_WebApi.Services.AITools.Assessment;

/// <summary>Product identity directory, not a clinical scoring/threshold registry.</summary>
public static class AssessmentScaleCatalog
{
    public const string Version = "project-online-scale-catalog-v1";
    public static IReadOnlySet<string> OnlineGames { get; } = new HashSet<string>(StringComparer.Ordinal)
    { "饥饿的小鸟", "色盲测试", "小镇警探", "打地鼠", "舒尔特方格", "沙滩坦克", "记忆速配" };
    public static IReadOnlyDictionary<int, string> Names { get; } = new System.Collections.ObjectModel.ReadOnlyDictionary<int, string>(new Dictionary<int, string>
    {
        [1] = "AD8认知障碍自评表", [2] = "ADL量表-Barthel指数", [3] = "ADL日常生活能力量表",
        [4] = "Beck抑郁量表", [5] = "GAD-7广泛性焦虑量表", [6] = "Rivermead行为记忆量表记录表",
        [7] = "汉密尔顿抑郁量表", [8] = "简易认知状态评价量表", [9] = "简易智力状态评估表",
        [10] = "老年人认知功能筛查量表", [11] = "蒙特利尔认知评估量表记录表",
        [12] = "认知障碍分级评定表", [13] = "色盲色弱检测国际量表"
    });

    public static bool Matches(int type, string? name)
    {
        if (name == null || !Names.TryGetValue(type, out var canonical)) return false;
        var normalized = string.Concat(name.Where(c => !char.IsWhiteSpace(c)));
        return normalized == canonical || (type == 1 && normalized == "AD8");
    }
}
