using AI_WebApi.Model.AITools;
using AI_WebApi.Model.Domain.IReTour;
using AI_WebApi.Model.DTOs.AITools;
using Newtonsoft.Json.Linq;
using System.Globalization;
using Newtonsoft.Json;

namespace AI_WebApi.Services.AITools.IReTour;

public sealed class IReTourMetricDefinition
{
    public string MetricCode { get; init; }
    public string DisplayName { get; init; }
    public IReadOnlyList<string> FieldAliases { get; init; }
    public decimal? Minimum { get; init; }
    public decimal? Maximum { get; init; }
    public string ValueKind { get; init; } = "number";
    /// <summary>Units are unconfirmed until the vendor signs them off; null means unconfirmed.</summary>
    public string? Unit { get; init; }
    public bool ZeroMeansMissing { get; init; }
}

public sealed class IReTourPlanParameterDefinition
{
    public string Code { get; init; }
    public string DisplayName { get; init; }
    public string FieldAlias { get; init; }
    public string ValueKind { get; init; } = "bool";
}

/// <summary>
/// Per-item summary parsers. Each parser declares its schema family, the
/// supported metric aliases and plan parameters; the shared engine extracts
/// only whitelisted fields, so name/phone or raw arrays inside the JSON are
/// never surfaced to the model.
/// </summary>
public interface IIReTourSummaryParser
{
    string SchemaFamily { get; }
    int ItemId { get; }
    IReadOnlyList<IReTourMetricDefinition> Metrics { get; }
    IReadOnlyList<IReTourPlanParameterDefinition> Parameters { get; }
}

public class StraightSummaryParser : IIReTourSummaryParser
{
    public string SchemaFamily => "straight_summary_v1";
    public virtual int ItemId => 0;

    public IReadOnlyList<IReTourMetricDefinition> Metrics { get; } =
        StraightMetrics("straight");

    public IReadOnlyList<IReTourPlanParameterDefinition> Parameters { get; } =
        new[]
        {
            Parameter("start_from_first", "起始方向", "startFromFirst"),
            Parameter("start_from_first_g", "起始方向", "g_startFromFirst")
        };

    internal static IReadOnlyList<IReTourMetricDefinition> StraightMetrics(
        string prefix) => new[]
    {
        Metric($"{prefix}_speed", "平均速度", new[] { "avg_Speed", "g_averageWalkingSpeed" }, 0m, 100m),
        Metric($"{prefix}_average_step_time", "平均步时", new[] { "avg_StepTime", "g_averageStepTime" }, 0m, 600m),
        Metric($"{prefix}_max_step_time", "最大步时", new[] { "max_StepTime", "g_maxStepTime" }, 0m, 600m),
        Metric($"{prefix}_min_step_time", "最小时步", new[] { "min_StepTime", "g_minStepTime" }, 0m, 600m),
        Metric($"{prefix}_distance", "距离", new[] { "distance", "g_walkingDistance" }, 0m, 100000m),
        Metric($"{prefix}_effective_count", "有效次数", new[] { "eff_Count", "g_stepOnExact" }, 0m, null),
        Metric($"{prefix}_total_lights", "灯光总数", new[] { "total_Light", "g_numberOfLights" }, 0m, null)
    };

    internal static IReTourMetricDefinition Metric(
        string code,
        string displayName,
        IReadOnlyList<string> aliases,
        decimal? min,
        decimal? max) => new()
        {
            MetricCode = code,
            DisplayName = displayName,
            FieldAliases = aliases,
            Minimum = min,
            Maximum = max
        };

    internal static IReTourPlanParameterDefinition Parameter(
        string code,
        string displayName,
        string alias) => new()
        {
            Code = code,
            DisplayName = displayName,
            FieldAlias = alias
        };
}

public sealed class StraightSecondarySummaryParser : StraightSummaryParser
{
    public override int ItemId => 1;
}

public class ReactionSummaryParser : IIReTourSummaryParser
{
    public string SchemaFamily => "reaction_summary_v1";
    public virtual int ItemId => 2;

    public IReadOnlyList<IReTourMetricDefinition> Metrics { get; } = new[]
    {
        StraightSummaryParser.Metric("reaction_correct_rate", "正确率", new[] { "correctRate", "g_correctRate" }, 0m, 1m),
        StraightSummaryParser.Metric("reaction_error_rate", "错误率", new[] { "errorRate", "g_errorRate" }, 0m, 1m),
        StraightSummaryParser.Metric("reaction_completion_rate", "完成率", new[] { "completionRate", "g_completionRate" }, 0m, 1m),
        StraightSummaryParser.Metric("reaction_correct_count", "正确次数", new[] { "correctSum", "g_correctNum" }, 0m, null),
        StraightSummaryParser.Metric("reaction_error_count", "错误次数", new[] { "errorSum", "g_errorNum" }, 0m, null),
        StraightSummaryParser.Metric("reaction_average_speed", "平均反应速度", new[] { "avg_ReactionSpeed", "g_averageSpeed" }, 0m, null),
        StraightSummaryParser.Metric("reaction_max_speed", "最大反应速度", new[] { "max_ReactionSpeed", "g_maxSpeed" }, 0m, null),
        StraightSummaryParser.Metric("reaction_total_lights", "灯光总数", new[] { "total_Lights", "g_lightCount" }, 0m, null)
    };

    public IReadOnlyList<IReTourPlanParameterDefinition> Parameters { get; } =
        Array.Empty<IReTourPlanParameterDefinition>();
}

public sealed class ReactionSecondarySummaryParser : ReactionSummaryParser
{
    public override int ItemId => 3;
}

public sealed class BalanceSummaryParser : IIReTourSummaryParser
{
    public string SchemaFamily => "balance_summary_v1";
    public int ItemId => 4;

    public IReadOnlyList<IReTourMetricDefinition> Metrics { get; } = new[]
    {
        StraightSummaryParser.Metric("balance_left_deviation_rate", "左侧偏差率", new[] { "left_DeviationRate", "g_leftDeviationRate" }, 0m, 1m),
        StraightSummaryParser.Metric("balance_right_deviation_rate", "右侧偏差率", new[] { "right_DeviationRate", "g_rightDeviationRate" }, 0m, 1m),
        StraightSummaryParser.Metric("balance_left_red_times", "左侧红色次数", new[] { "left_RedTimes", "g_leftRedTimes" }, 0m, null),
        StraightSummaryParser.Metric("balance_left_yellow_times", "左侧黄色次数", new[] { "left_YellowTimes", "g_leftYellowTimes" }, 0m, null),
        StraightSummaryParser.Metric("balance_left_orange_times", "左侧橙色次数", new[] { "left_OrangeTimes", "g_leftOrangeTimes" }, 0m, null),
        StraightSummaryParser.Metric("balance_right_red_times", "右侧红色次数", new[] { "right_RedTimes", "g_rightRedTimes" }, 0m, null),
        StraightSummaryParser.Metric("balance_right_yellow_times", "右侧黄色次数", new[] { "right_YellowTimes", "g_rightYellowTimes" }, 0m, null),
        StraightSummaryParser.Metric("balance_right_orange_times", "右侧橙色次数", new[] { "right_OrangeTimes", "g_rightOrangeTimes" }, 0m, null)
    };

    public IReadOnlyList<IReTourPlanParameterDefinition> Parameters { get; } =
        new[] { StraightSummaryParser.Parameter("start_from_left", "起始侧", "startFromLeft") };
}

public sealed class VibrationSummaryParser : IIReTourSummaryParser
{
    public string SchemaFamily => "vibration_summary_v1";
    public int ItemId => 5;
    public IReadOnlyList<IReTourMetricDefinition> Metrics { get; } =
        Array.Empty<IReTourMetricDefinition>();
    public IReadOnlyList<IReTourPlanParameterDefinition> Parameters { get; } =
        Array.Empty<IReTourPlanParameterDefinition>();
}

public sealed class TransverseSummaryParser : IIReTourSummaryParser
{
    public string SchemaFamily => "transverse_summary_v1";
    public int ItemId => 6;

    public IReadOnlyList<IReTourMetricDefinition> Metrics { get; } = new[]
    {
        StraightSummaryParser.Metric("transverse_speed", "平均速度", new[] { "avg_Speed", "g_transSpeed" }, 0m, 100m),
        StraightSummaryParser.Metric("transverse_average_step_time", "平均步时", new[] { "avg_StepTime", "g_averageStepTime" }, 0m, 600m),
        StraightSummaryParser.Metric("transverse_max_step_time", "最大步时", new[] { "max_StepTime", "g_maxStepTime" }, 0m, 600m),
        StraightSummaryParser.Metric("transverse_min_step_time", "最小时步", new[] { "min_StepTime", "g_minStepTime" }, 0m, 600m),
        StraightSummaryParser.Metric("transverse_distance", "距离", new[] { "distance", "g_walkingDistance" }, 0m, 100000m),
        StraightSummaryParser.Metric("transverse_added_distance", "追加距离", new[] { "distance_add", "g_transAdditionalDistance" }, 0m, 100000m),
        StraightSummaryParser.Metric("transverse_effective_count", "有效次数", new[] { "eff_Count", "g_stepOnExact" }, 0m, null),
        StraightSummaryParser.Metric("transverse_total_lights", "灯光总数", new[] { "total_Light", "g_numberOfLights" }, 0m, null),
        StraightSummaryParser.Metric("transverse_trail_length", "轨迹长度", new[] { "g_trailLength" }, 0m, null),
        StraightSummaryParser.Metric("transverse_training_duration", "训练时长", new[] { "g_transTrainingDuration" }, 0m, null)
    };

    public IReadOnlyList<IReTourPlanParameterDefinition> Parameters { get; } =
        new[]
        {
            StraightSummaryParser.Parameter("is_start_first", "起始方向", "isStartFirst"),
            StraightSummaryParser.Parameter("is_start_first_g", "起始方向", "g_isStartFromFirst")
        };
}

public sealed class StrideSummaryParser : IIReTourSummaryParser
{
    public string SchemaFamily => "stride_summary_v1";
    public int ItemId => 7;
    public IReadOnlyList<IReTourMetricDefinition> Metrics { get; } =
        StraightSummaryParser.StraightMetrics("stride")
            .Append(
                StraightSummaryParser.Metric(
                    "stride_added_distance",
                    "追加距离",
                    new[] { "distance_add" },
                    0m,
                    100000m))
            .ToList();
    public IReadOnlyList<IReTourPlanParameterDefinition> Parameters { get; } =
        new StraightSummaryParser().Parameters.ToList();
}

public sealed class SidewaySummaryParser : IIReTourSummaryParser
{
    public string SchemaFamily => "sideway_summary_v1";
    public int ItemId => 8;

    public IReadOnlyList<IReTourMetricDefinition> Metrics { get; } = new[]
    {
        StraightSummaryParser.Metric("sideway_duration", "侧向时长", new[] { "g_sidewayDuration" }, 0m, null),
        StraightSummaryParser.Metric("sideway_walking_distance", "行走距离", new[] { "g_walkingDistance" }, 0m, 100000m),
        StraightSummaryParser.Metric("sideway_average_walking_speed", "平均行走速度", new[] { "g_averageWalkingSpeed" }, 0m, 100m),
        StraightSummaryParser.Metric("sideway_average_step_time", "平均步时", new[] { "g_averageStepTime" }, 0m, 600m),
        StraightSummaryParser.Metric("sideway_max_step_time", "最大步时", new[] { "g_maxStepTime" }, 0m, 600m),
        StraightSummaryParser.Metric("sideway_min_step_time", "最小时步", new[] { "g_minStepTime" }, 0m, 600m),
        StraightSummaryParser.Metric("sideway_step_on_exact", "踩准次数", new[] { "g_stepOnExact" }, 0m, null),
        StraightSummaryParser.Metric("sideway_trail_length", "轨迹长度", new[] { "g_trailLength" }, 0m, null),
        StraightSummaryParser.Metric("sideway_number_of_lights", "灯光总数", new[] { "g_numberOfLights" }, 0m, null),
        StraightSummaryParser.Metric("sideway_training_duration", "训练时长", new[] { "g_trainingDuration" }, 0m, null)
    };

    public IReadOnlyList<IReTourPlanParameterDefinition> Parameters { get; } =
        new[] { StraightSummaryParser.Parameter("start_from_first", "起始方向", "g_startFromFirst") };
}

public sealed class GaitAssessmentSummaryParser : IIReTourSummaryParser
{
    public string SchemaFamily => "gait_assessment_summary_v1";
    public int ItemId => 9;

    public IReadOnlyList<IReTourMetricDefinition> Metrics { get; } = new[]
    {
        // 生产数据主族为嵌套 g_assessmentResult.{...}；顶层同名字段为旧族兜底。
        StraightSummaryParser.Metric("gait_speed", "速度", new[] { "g_assessmentResult.speed", "speed" }, 0m, 100m),
        StraightSummaryParser.Metric("gait_cadence", "步频", new[] { "g_assessmentResult.cadence", "cadence" }, 0m, null),
        StraightSummaryParser.Metric("gait_step_length", "步长", new[] { "g_assessmentResult.step_length", "step_length" }, 0m, 10000m),
        StraightSummaryParser.Metric("gait_step_num", "步数", new[] { "g_assessmentResult.step_num", "step_num" }, 0m, null),
        StraightSummaryParser.Metric("gait_step_width", "步宽", new[] { "g_assessmentResult.step_width", "step_width" }, 0m, 10000m),
        StraightSummaryParser.Metric("gait_assess_duration", "评估时长", new[] { "g_assessDuration" }, 0m, null),
        StraightSummaryParser.Metric("gait_training_duration", "训练时长", new[] { "g_trainingDuration" }, 0m, null)
    };

    public IReadOnlyList<IReTourPlanParameterDefinition> Parameters { get; } =
        Array.Empty<IReTourPlanParameterDefinition>();
}

public sealed class IReTourSummaryParserRegistry
{
    private static readonly IReadOnlyDictionary<int, IIReTourSummaryParser> Parsers =
        new Dictionary<int, IIReTourSummaryParser>
        {
            [0] = new StraightSummaryParser(),
            [1] = new StraightSecondarySummaryParser(),
            [2] = new ReactionSummaryParser(),
            [3] = new ReactionSecondarySummaryParser(),
            [4] = new BalanceSummaryParser(),
            [5] = new VibrationSummaryParser(),
            [6] = new TransverseSummaryParser(),
            [7] = new StrideSummaryParser(),
            [8] = new SidewaySummaryParser(),
            [9] = new GaitAssessmentSummaryParser()
        };

    public static IIReTourSummaryParser? Resolve(int itemId) =>
        Parsers.TryGetValue(itemId, out var parser) ? parser : null;
}

public static class IReTourSummaryExtraction
{
    /// <summary>
    /// Extracts whitelisted metrics and plan parameters from a summary details
    /// JSON payload. Raw arrays, name/phone fields and unknown keys are never
    /// returned. NaN/Infinity strings become null normalized values with the
    /// non-finite exclusion code; out-of-range and negative values are marked
    /// invalid without dropping the session.
    /// </summary>
    public static (
        IReadOnlyList<IReTourMetricFact> Metrics,
        IReadOnlyList<IReTourPlanParameterDto> Parameters,
        IReadOnlyList<ToolIssueDto> Issues)
        Parse(string detailsJson, IIReTourSummaryParser parser)
    {
        var metrics = new List<IReTourMetricFact>();
        var parameters = new List<IReTourPlanParameterDto>();
        var issues = new List<ToolIssueDto>();

        JObject root;
        try
        {
            if (detailsJson.Length > 524288) throw new JsonReaderException("Summary exceeds size limit");
            using var reader = new JsonTextReader(new StringReader(detailsJson)) { MaxDepth = 32, FloatParseHandling = FloatParseHandling.Decimal };
            root = JObject.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
        }
        catch (Exception)
        {
            issues.Add(new ToolIssueDto
            {
                Code = IReTourReasonCodes.SchemaUnsupported,
                Severity = "error",
                Scope = "summary",
                Action = "exclude_metrics",
                PatientMessage = "本次汇总结果无法解析。"
            });
            return (metrics, parameters, issues);
        }

        foreach (var definition in parser.Metrics)
        {
            foreach (var alias in definition.FieldAliases)
            {
                // 点路径别名（如 g_assessmentResult.speed）用 SelectToken 解析嵌套对象。
                var token = root.SelectToken(alias);
                if (token == null || token.Type == JTokenType.Null)
                {
                    continue;
                }

                decimal? raw = ParseDecimal(token);
                string? status = null;
                var aliases = definition.FieldAliases.Select(a => root.SelectToken(a)).Where(t => t != null && t.Type != JTokenType.Null).ToArray();
                if (aliases.Length > 1 && aliases.Any(t => ParseDecimal(t!) != raw))
                {
                    status = "invalid";
                    issues.Add(new ToolIssueDto { Code = "IRETOUR_METRIC_ALIAS_CONFLICT", Scope = $"metric:{definition.MetricCode}", Action = "exclude_value", PatientMessage = "同一指标存在不一致的字段值，已排除。" });
                }
                if (raw == null)
                {
                    status = "invalid";
                    issues.Add(new ToolIssueDto
                    {
                        Code = IReTourReasonCodes.NonFiniteMetricExcluded,
                        Severity = "warning",
                        Scope = $"metric:{definition.MetricCode}",
                        Action = "exclude_value",
                        PatientMessage = "部分指标数值无法确认。"
                    });
                }
                else if (definition.Minimum.HasValue && raw < definition.Minimum.Value ||
                         definition.Maximum.HasValue && raw > definition.Maximum.Value)
                {
                    status = "invalid";
                    issues.Add(new ToolIssueDto
                    {
                        Code = IReTourReasonCodes.MetricOutOfRange,
                        Severity = "warning",
                        Scope = $"metric:{definition.MetricCode}",
                        Action = "exclude_value",
                        PatientMessage = "部分指标数值超出可信范围。"
                    });
                }
                else if (definition.Unit == null)
                {
                    issues.Add(new ToolIssueDto
                    {
                        Code = IReTourReasonCodes.MetricUnitUnconfirmed,
                        Severity = "warning",
                        Scope = $"metric:{definition.MetricCode}",
                        Action = "none",
                        PatientMessage = "该指标单位尚未确认。"
                    });
                }

                metrics.Add(new IReTourMetricFact
                {
                    MetricCode = definition.MetricCode,
                    DisplayName = definition.DisplayName,
                    RawValue = raw,
                    NormalizedValue = status == null ? raw : null,
                    Unit = definition.Unit,
                    ValueStatus = status ?? "valid",
                    SourceField = alias
                });
                break;
            }
        }

        foreach (var definition in parser.Parameters)
        {
            var token = root.SelectToken(definition.FieldAlias);
            if (token == null || token.Type == JTokenType.Null)
            {
                continue;
            }

            // Settings are booleans in the verified registry; never forward arbitrary JSON/text.
            if (!bool.TryParse(token.ToString(), out var booleanValue)) continue;
            parameters.Add(new IReTourPlanParameterDto
            {
                Code = definition.Code,
                DisplayName = definition.DisplayName,
                Value = booleanValue.ToString().ToLowerInvariant()
            });
        }

        return (metrics, parameters, issues);
    }

    private static decimal? ParseDecimal(JToken token)
    {
        var text = token.ToString().Trim();
        if (text is "NaN" or "Infinity" or "-Infinity" || text.Length == 0)
        {
            return null;
        }

        return token.Type is JTokenType.Float or JTokenType.Integer or JTokenType.String &&
            decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value : null;
    }
}
