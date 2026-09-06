using System.Globalization;
using AI_WebApi.Model.AITools;
using AI_WebApi.Model.Domain.IReGo;
using AI_WebApi.Model.DTOs.AITools;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AI_WebApi.Services.AITools.IReGo;

/// <summary>
/// Parses the currently observed plan/template JSON into the existing
/// IReGoContentBlockDto and MetricFactDto shapes. It is deliberately
/// conservative: unknown content and units remain unknown.
/// </summary>
public static class IReGoPlanParser
{
    public static ParsedIReGoPlan Parse(
        string? planDetails,
        string? templateDetails)
    {
        var candidate = !string.IsNullOrWhiteSpace(planDetails)
            ? planDetails
            : templateDetails;
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return new ParsedIReGoPlan();
        }

        JToken root;
        try
        {
            root = JToken.Parse(candidate, new JsonLoadSettings
            {
                DuplicatePropertyNameHandling =
                    DuplicatePropertyNameHandling.Replace
            });
        }
        catch (JsonException exception)
        {
            return new ParsedIReGoPlan
            {
                SchemaFamily = "invalid",
                Issues = new[]
                {
                    Issue(
                        IReGoReasonCodes.PlanDetailsInvalid,
                        "warning",
                        "plan_schema",
                        "retain_plan_without_blocks",
                        exception.Message)
                }
            };
        }

        var items = ResolveItems(root);
        var ordinals = new Dictionary<string, int>(StringComparer.Ordinal);
        var blocks = new List<IReGoContentBlockDto>();
        var sequence = 0;

        foreach (var item in items)
        {
            sequence++;
            var reportMode = ReadInt(item, "ReportMode") ??
                ReportModeForType(ReadString(item, "type"));
            var balanceMode =
                ReadInt(item, "BalanceMode") ??
                ReadInt(item, "Balance_Mode") ??
                ReadInt(item, "SitAndStand_Mode");
            var (rehabContent, interactionType, sourceName) =
                Classify(item, reportMode, balanceMode);
            var ordinal = ordinals.TryGetValue(rehabContent, out var current)
                ? current + 1
                : 1;
            ordinals[rehabContent] = ordinal;
            var displayName = BuildDisplayName(
                rehabContent,
                interactionType,
                sourceName,
                ordinal);
            var metrics = ExtractMetrics(item, rehabContent);

            blocks.Add(new IReGoContentBlockDto
            {
                Sequence = sequence,
                ReportMode = reportMode,
                BalanceMode = balanceMode,
                RehabContent = rehabContent,
                InteractionType = interactionType,
                Name = displayName,
                DisplayName = displayName,
                TrainingModeName = ResolveTrainingModeName(
                    rehabContent,
                    metrics),
                Metrics = metrics
            });
        }

        return new ParsedIReGoPlan
        {
            ParseSucceeded = true,
            SchemaFamily = root is JObject rootObject &&
                rootObject.GetValue(
                    "templateData",
                    StringComparison.OrdinalIgnoreCase) is JArray
                    ? "robotdb_plan_template_v1"
                    : root.Type switch
                    {
                        JTokenType.Array => "irego_plan_array",
                        JTokenType.Object => "irego_plan_object",
                        _ => "unknown"
                    },
            Blocks = blocks
        };
    }

    private static IReadOnlyList<JObject> ResolveItems(JToken root)
    {
        if (root is JArray array)
        {
            return array.OfType<JObject>().ToArray();
        }

        if (root is not JObject obj)
        {
            return Array.Empty<JObject>();
        }

        foreach (var name in new[]
                 {
                     "templateData",
                     "Details", "DetailList", "PlanList", "Items", "Tasks"
                 })
        {
            if (obj.GetValue(name, StringComparison.OrdinalIgnoreCase) is
                JArray namedArray)
            {
                return namedArray.OfType<JObject>().ToArray();
            }
        }

        var firstObjectArray = obj
            .Descendants()
            .OfType<JArray>()
            .FirstOrDefault(x => x.OfType<JObject>().Any());
        return firstObjectArray == null
            ? new[] { obj }
            : firstObjectArray.OfType<JObject>().ToArray();
    }

    private static IReadOnlyList<MetricFactDto> ExtractMetrics(
        JObject item,
        string rehabContent)
    {
        var metrics = new List<MetricFactDto>();
        AddMetric(
            item,
            "Walk_Mode",
            "plan_walking_mode",
            "templateData[].Walk_Mode",
            null,
            "source_declared",
            metrics);
        AddMetric(
            item,
            "Walk_Time",
            "plan_set_time",
            "templateData[].Walk_Time",
            null,
            "unverified",
            metrics);
        AddMetric(
            item,
            "WInitiative_Force",
            "plan_assistance",
            "templateData[].WInitiative_Force",
            null,
            "unverified",
            metrics);
        AddMetric(
            item,
            "WResistance_Force",
            "plan_resistance",
            "templateData[].WResistance_Force",
            null,
            "unverified",
            metrics);
        AddMetric(
            item,
            "WFollowing_Imponderability",
            "plan_weight_loss",
            "templateData[].WFollowing_Imponderability",
            null,
            "unverified",
            metrics);
        AddMetric(
            item,
            "WInitiative_Imponderability",
            "plan_weight_loss",
            "templateData[].WInitiative_Imponderability",
            null,
            "unverified",
            metrics);
        AddMetric(
            item,
            "WResistance_Imponderability",
            "plan_weight_loss",
            "templateData[].WResistance_Imponderability",
            null,
            "unverified",
            metrics);
        AddMetric(
            item,
            "WFollowing_FixedDistance",
            "plan_walking_distance",
            "templateData[].WFollowing_FixedDistance",
            null,
            "unverified",
            metrics);
        AddMetric(
            item,
            "WInitiative_FixedDistance",
            "plan_walking_distance",
            "templateData[].WInitiative_FixedDistance",
            null,
            "unverified",
            metrics);
        AddMetric(
            item,
            "WResistance_FixedDistance",
            "plan_walking_distance",
            "templateData[].WResistance_FixedDistance",
            null,
            "unverified",
            metrics);
        AddMetric(
            item,
            "SitAndStand_Mode",
            "plan_sit_stand_mode",
            "templateData[].SitAndStand_Mode",
            null,
            "source_declared",
            metrics);
        AddMetric(
            item,
            "SitAndStand_Time",
            "plan_set_time",
            "templateData[].SitAndStand_Time",
            null,
            "unverified",
            metrics);
        AddMetric(
            item,
            "SInitiative_Count",
            "plan_sit_stand_repetitions",
            "templateData[].SInitiative_Count",
            "count",
            "source_declared",
            metrics);
        AddMetric(
            item,
            "SInitiative_Imponderability",
            "plan_weight_loss",
            "templateData[].SInitiative_Imponderability",
            null,
            "unverified",
            metrics);
        AddMetric(
            item,
            "Gaming_Mode",
            "plan_game_mode",
            "templateData[].Gaming_Mode",
            null,
            "source_declared",
            metrics);
        AddMetric(
            item,
            "Gaming_Time",
            "plan_set_time",
            "templateData[].Gaming_Time",
            null,
            "unverified",
            metrics);
        AddMetric(
            item,
            "G_Intensity",
            "plan_game_intensity",
            "templateData[].G_Intensity",
            null,
            "source_declared",
            metrics);
        AddMetric(
            item,
            "Balance_Mode",
            "plan_balance_mode",
            "templateData[].Balance_Mode",
            null,
            "source_declared",
            metrics);
        AddMetric(
            item,
            "Balance_Time",
            "plan_set_time",
            "templateData[].Balance_Time",
            null,
            "unverified",
            metrics);
        AddMetric(
            item,
            "BFRT_Imponderability",
            "plan_weight_loss",
            "templateData[].BFRT_Imponderability",
            null,
            "unverified",
            metrics);
        AddMetric(
            item,
            "BBoth_Imponderability",
            "plan_weight_loss",
            "templateData[].BBoth_Imponderability",
            null,
            "unverified",
            metrics);
        AddMetric(
            item,
            "BLeft_Imponderability",
            "plan_weight_loss",
            "templateData[].BLeft_Imponderability",
            null,
            "unverified",
            metrics);
        AddMetric(
            item,
            "BRight_Imponderability",
            "plan_weight_loss",
            "templateData[].BRight_Imponderability",
            null,
            "unverified",
            metrics);
        AddMetric(
            item,
            "time",
            "plan_set_time",
            "Details[].time",
            null,
            "unverified",
            metrics);
        AddMetric(
            item,
            "speed",
            rehabContent == "walking" ? "plan_walking_speed" : "plan_speed",
            "Details[].speed",
            rehabContent == "walking" ? "m/s" : null,
            rehabContent == "walking" ? "confirmed" : "unverified",
            metrics);
        AddMetric(
            item,
            "assistance",
            "plan_assistance",
            "Details[].assistance",
            "N",
            "confirmed",
            metrics);
        AddMetric(
            item,
            "resistance",
            "plan_resistance",
            "Details[].resistance",
            "N",
            "confirmed",
            metrics);
        AddMetric(
            item,
            "weightLoss",
            "plan_weight_loss",
            "Details[].weightLoss",
            "kg",
            "confirmed",
            metrics);
        AddMetric(
            item,
            "sitTime",
            "plan_sit_hold_duration",
            "Details[].sitTime",
            "s",
            "confirmed",
            metrics);
        AddMetric(
            item,
            "standTime",
            "plan_stand_hold_duration",
            "Details[].standTime",
            "s",
            "confirmed",
            metrics);
        return metrics;
    }

    private static void AddMetric(
        JObject item,
        string lookupField,
        string metricCode,
        string sourceField,
        string? unit,
        string unitStatus,
        ICollection<MetricFactDto> target)
    {
        var property = item
            .DescendantsAndSelf()
            .OfType<JProperty>()
            .FirstOrDefault(x =>
                x.Name.Equals(lookupField, StringComparison.OrdinalIgnoreCase));
        if (property == null)
        {
            return;
        }

        var normalized = ReadFiniteDecimal(property.Value, out var valueStatus);
        target.Add(new MetricFactDto
        {
            MetricCode = metricCode,
            RawValue = ToRawValue(property.Value),
            NormalizedValue = normalized,
            ValueStatus = valueStatus,
            Unit = unit,
            UnitStatus = unitStatus,
            DataStage = "plan_derived",
            SourceField = sourceField,
            DisplayName = IReGoMetricDisplayRegistry.Label(metricCode),
            SourceKind = "raw",
            MetricEvidenceLevel = unitStatus == "confirmed"
                ? "confirmed_source_fact"
                : "source_fact"
        });
    }

    private static string? ResolveTrainingModeName(
        string rehabContent,
        IReadOnlyList<MetricFactDto> metrics)
    {
        var metricCode = rehabContent is "walking" or "single_walking"
            ? "plan_walking_mode"
            : rehabContent is "sit_stand" or "single_sit"
                ? "plan_sit_stand_mode"
                : null;
        var mode = metricCode == null
            ? null
            : metrics.FirstOrDefault(x =>
                x.MetricCode == metricCode &&
                x.ValueStatus == "valid")?.NormalizedValue;
        return (metricCode, mode) switch
        {
            ("plan_walking_mode", 0m) => "跟随",
            ("plan_walking_mode", 1m) => "助力",
            ("plan_walking_mode", 2m) => "阻力",
            ("plan_sit_stand_mode", 0m) => "主动",
            ("plan_sit_stand_mode", 1m) => "被动",
            _ => null
        };
    }

    private static (
        string RehabContent,
        string InteractionType,
        string? SourceName) Classify(
        JObject item,
        int? reportMode,
        int? balanceMode)
    {
        if (reportMode.HasValue)
        {
            return reportMode.Value switch
            {
                1 => ("walking", "direct_training", null),
                2 => ("sit_stand", "direct_training", null),
                3 => (
                    "game",
                    ReadBool(item, "IsVR") == true ? "vr_game" : "game",
                    ResolveGameName(item, "GameMode")),
                4 when balanceMode is >= 5 and <= 8 =>
                    ("balance_assessment", "assessment", null),
                4 => ("balance_training", "direct_training", null),
                5 => ("single_walking", "direct_training", null),
                6 => ("single_sit", "direct_training", null),
                _ => ("unknown", "direct_training", null)
            };
        }

        var text = new[]
            {
                ReadString(item, "rehabContent"),
                ReadString(item, "contentType"),
                ReadString(item, "type"),
                ReadString(item, "name")
            }
            .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))
            ?.Trim();
        if (!string.IsNullOrWhiteSpace(text))
        {
            var lowered = text.ToLowerInvariant();
            if (lowered == "gaming")
            {
                var gameMode = ReadInt(item, "Gaming_Mode");
                return (
                    "game",
                    "game",
                    IReGoGameModeRegistry.DisplayName(gameMode) ??
                    (gameMode.HasValue ? $"GameMode {gameMode.Value}" : null));
            }

            if (lowered == "singlewalking")
            {
                return ("single_walking", "direct_training", null);
            }

            if (lowered == "singlesit")
            {
                return ("single_sit", "direct_training", null);
            }

            if (lowered == "joint")
            {
                return ("joint_training", "direct_training", null);
            }

            if (lowered.Contains("walk") || text.Contains("步行"))
            {
                return ("walking", "direct_training", null);
            }

            if (lowered.Contains("sit") || text.Contains("坐站"))
            {
                return ("sit_stand", "direct_training", null);
            }

            if (lowered.Contains("balance") || text.Contains("平衡"))
            {
                var mode = ReadInt(item, "Balance_Mode");
                return mode is >= 5 and <= 8
                    ? ("balance_assessment", "assessment", null)
                    : ("balance_training", "direct_training", null);
            }

            if (lowered.Contains("game") || text.Contains("游戏"))
            {
                return ("game", "game", null);
            }
        }

        if (item.GetValue("sitTime", StringComparison.OrdinalIgnoreCase) != null ||
            item.GetValue("standTime", StringComparison.OrdinalIgnoreCase) != null)
        {
            return ("sit_stand", "direct_training", text);
        }

        return ("unknown", "direct_training", text);
    }

    private static string BuildDisplayName(
        string rehabContent,
        string interactionType,
        string? sourceName,
        int ordinal)
    {
        if (!string.IsNullOrWhiteSpace(sourceName))
        {
            return sourceName.Trim();
        }

        return rehabContent switch
        {
            "walking" => $"计划步行训练第{ordinal}段",
            "sit_stand" => $"计划坐站训练第{ordinal}段",
            "balance_training" => $"计划平衡训练第{ordinal}段",
            "balance_assessment" => $"计划平衡评估第{ordinal}段",
            "single_joint" => $"计划单关节训练第{ordinal}段",
            "single_walking" => $"计划单侧步行训练第{ordinal}段",
            "single_sit" => $"计划单侧坐站训练第{ordinal}段",
            "joint_training" => $"计划关节训练第{ordinal}段",
            "game" when interactionType == "vr_game" =>
                $"计划VR游戏第{ordinal}段",
            "game" => $"计划训练游戏第{ordinal}段",
            _ => $"计划训练第{ordinal}段"
        };
    }

    private static string? ResolveGameName(JObject item, string modeField)
    {
        var sourceName = ReadString(item, "GameName");
        if (!string.IsNullOrWhiteSpace(sourceName))
        {
            return sourceName;
        }

        var gameMode = ReadInt(item, modeField) ??
            ReadInt(item, "Gaming_Mode");
        return IReGoGameModeRegistry.DisplayName(gameMode) ??
            (gameMode.HasValue ? $"GameMode {gameMode.Value}" : null);
    }

    private static int? ReportModeForType(string? type) =>
        type?.Trim().ToLowerInvariant() switch
        {
            "walk" => 1,
            "sitandstand" => 2,
            "gaming" => 3,
            "balance" => 4,
            "singlewalking" => 5,
            "singlesit" => 6,
            _ => null
        };

    private static decimal? ReadFiniteDecimal(
        JToken token,
        out string status)
    {
        if (token.Type is JTokenType.Integer or JTokenType.Float &&
            decimal.TryParse(
                token.ToString(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var numeric))
        {
            status = "valid";
            return numeric;
        }

        if (token.Type == JTokenType.String &&
            decimal.TryParse(
                token.Value<string>(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out numeric))
        {
            status = "valid";
            return numeric;
        }

        status = token.Type is JTokenType.Null or JTokenType.Undefined
            ? "missing"
            : "invalid";
        return null;
    }

    private static object? ToRawValue(JToken token) =>
        token.Type switch
        {
            JTokenType.Null or JTokenType.Undefined => null,
            JTokenType.Integer => token.Value<long>(),
            JTokenType.Float => token.Value<double>(),
            JTokenType.Boolean => token.Value<bool>(),
            JTokenType.String => token.Value<string>(),
            _ => token.ToString(Formatting.None)
        };

    private static int? ReadInt(JObject obj, string name)
    {
        var token = obj.GetValue(name, StringComparison.OrdinalIgnoreCase);
        return token != null && int.TryParse(
            token.ToString(),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var value)
            ? value
            : null;
    }

    private static bool? ReadBool(JObject obj, string name)
    {
        var token = obj.GetValue(name, StringComparison.OrdinalIgnoreCase);
        return token?.Type == JTokenType.Boolean ? token.Value<bool>() : null;
    }

    private static string? ReadString(JObject obj, string name)
    {
        var token = obj.GetValue(name, StringComparison.OrdinalIgnoreCase);
        return token == null ||
               token.Type is JTokenType.Null or JTokenType.Undefined
            ? null
            : token.ToString();
    }

    private static ToolIssueDto Issue(
        string code,
        string severity,
        string scope,
        string action,
        string? detail = null) => new()
        {
            Code = code,
            Severity = severity,
            Scope = scope,
            Action = action,
            Detail = detail
        };
}
