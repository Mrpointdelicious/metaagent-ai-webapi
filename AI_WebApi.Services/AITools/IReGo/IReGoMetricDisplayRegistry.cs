namespace AI_WebApi.Services.AITools.IReGo;

internal static class IReGoMetricDisplayRegistry
{
    private static readonly IReadOnlyDictionary<string, string> Labels =
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
            ["sit_stand_repetitions"] = "完成次数",
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
            ["game_difficulty"] = "游戏难度",
            ["game_score"] = "游戏得分",
            ["game_score_rate"] = "单位时间得分",
            ["game_training_duration"] = "训练时长",
            ["game_star"] = "获得星数",
            ["balance_mode"] = "平衡模式",
            ["balance_set_or_record_time"] = "设置或记录时长",
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
            ["stability_field"] = "稳定范围"
        };

    private static readonly IReadOnlySet<string> ConfigurationCodes =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "walking_mode",
            "walking_set_time",
            "walking_display_set_time",
            "walking_display_assistance",
            "walking_display_resistance",
            "walking_display_weight",
            "walking_unloading",
            "sit_stand_mode",
            "sit_stand_set_time",
            "sit_stand_planned_repetitions",
            "sit_stand_unloading",
            "sit_stand_display_unloading",
            "game_mode",
            "game_set_time",
            "game_difficulty",
            "balance_mode",
            "balance_set_or_record_time",
            "balance_unloading"
        };

    private static readonly IReadOnlySet<string> AttributeOnlyCodes =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "walking_mode",
            "sit_stand_mode",
            "game_mode",
            "game_difficulty",
            "balance_mode"
        };

    public static string Label(string metricCode) =>
        Labels.TryGetValue(metricCode, out var label)
            ? label
            : metricCode;

    public static bool IsConfiguration(string metricCode) =>
        ConfigurationCodes.Contains(metricCode);

    public static bool IsChartEligible(string metricCode) =>
        !AttributeOnlyCodes.Contains(metricCode);
}
