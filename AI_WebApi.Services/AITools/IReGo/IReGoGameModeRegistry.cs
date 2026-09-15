namespace AI_WebApi.Services.AITools.IReGo;

/// <summary>
/// Confirmed robotdb_main GameMode mappings recovered from the legacy platform.
/// Unknown values intentionally remain unnamed.
/// </summary>
public static class IReGoGameModeRegistry
{
    public const string Version = "project-game-mode-2.0.0";

    public static string? DisplayName(int? gameMode) => gameMode switch
    {
        3 => "宇宙拼图",
        4 => "太空飞行",
        9 => "汪汪天使",
        10 => "打地鼠",
        11 => "跑酷猫",
        12 => "梦幻森林",
        13 => "太空漫步",
        14 => "循迹",
        23 => "欢乐盖大楼",
        43 => "海景别墅",
        44 => "丛林探险",
        _ => null
    };
}
