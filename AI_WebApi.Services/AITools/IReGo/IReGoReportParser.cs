using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using AI_WebApi.IServices.AITools;
using AI_WebApi.Model.AITools;
using AI_WebApi.Model.Domain.IReGo;
using AI_WebApi.Model.DTOs.AITools;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AI_WebApi.Services.AITools.IReGo;

/// <summary>
/// Deterministic parser for the current ReportDetails family.
/// Each ReportList element is one report record block, not an independent
/// rehabilitation project. ReportProcess is used only as a sequence check.
/// </summary>
public sealed class IReGoReportParser : IIReGoReportParser
{
    private static readonly MetricDefinition[] WalkingMetrics =
    {
        new("walking_mode", "WalkMode", null, "source_declared"),
        new("walking_set_time", "WTime", "min", "confirmed"),
        new("walking_force", "WForce", null, "unverified"),
        new("walking_training_duration", "WTrainingWalktime", "s", "confirmed"),
        new("walking_distance", "WTrainingWalkdistance", "m", "confirmed"),
        new("walking_distance", "WDistance", "m", "confirmed"),
        new("walking_step_count", "WSteps", "count", "candidate_protocol_mapping"),
        new("walking_average_speed", "WAvgSpeed", "m/s", "confirmed"),
        new("walking_max_speed", "WMaxSpeed", "m/s", "confirmed"),
        new("walking_average_frequency", "WAvgFrequency", null, "unverified"),
        new("walking_step_frequency", "WStepFreAgv", "step/min", "confirmed"),
        new("walking_step_length", "WStepLenAgv", "m", "confirmed"),
        new("walking_step_time", "WStepTAgv", "s", "confirmed"),
        new("walking_step_width", "WStepWidAgv", "mm", "confirmed"),
        new("walking_stance_average", "WStandAgv", null, "unverified"),
        new("walking_swing_average", "WSwingAgv", null, "unverified"),
        new("walking_double_support_average", "WDoubleAgv", null, "unverified"),
        new("walking_unloading", "WImponderability", "kg", "confirmed"),
        new("walking_average_gait_period", "WAvgGaitPeriod", "s", "confirmed"),
        new("walking_average_stride", "WAvgStep", "m", "confirmed"),
        new(
            "walking_display_time",
            "WalkReportList[].Time",
            "mm:ss",
            "confirmed",
            "Time",
            "duration_text"),
        new(
            "walking_display_set_time",
            "WalkReportList[].TotalTime",
            "min",
            "confirmed",
            "TotalTime"),
        new(
            "walking_display_assistance",
            "WalkReportList[].Assistance",
            "N",
            "confirmed",
            "Assistance"),
        new(
            "walking_display_resistance",
            "WalkReportList[].Resistance",
            "N",
            "confirmed",
            "Resistance"),
        new(
            "walking_display_weight",
            "WalkReportList[].Weight",
            "kg",
            "confirmed",
            "Weight")
    };

    private static readonly MetricDefinition[] SitStandMetrics =
    {
        new("sit_stand_mode", "SSMode", null, "source_declared"),
        new("sit_stand_set_time", "STime", "min", "confirmed"),
        new("sit_stand_planned_repetitions", "SNumber", "count", "source_declared"),
        new("sit_stand_unloading", "SImponderability", "kg", "confirmed"),
        new("sit_stand_training_duration", "STrainingSitTime", "s", "confirmed"),
        new("sit_stand_repetitions", "STrainingSitNum", "count", "confirmed"),
        new("sit_stand_cycle_average", "SrepCircleTimeAvg", "s", "confirmed"),
        new("sit_stand_cycle_maximum", "SrepCircleTimeMax", "s", "confirmed"),
        new("sit_stand_cycle_minimum", "SrepCircleTimeMin", "s", "confirmed"),
        new("sit_stand_stability_up_time", "StableSSTimeUp", "s", "confirmed"),
        new("sit_stand_stability_down_time", "StableSSTimeDown", "s", "confirmed"),
        new("sit_stand_average_time", "SAvgTime", "s", "confirmed"),
        new("sit_stand_min_time", "SMinTime", "s", "confirmed"),
        new("sit_stand_max_time", "SMaxTime", "s", "confirmed"),
        new("sit_stand_average_speed", "SAvgSpeed", null, "unverified"),
        new("sit_stand_average_distance", "SAvgDistance", null, "unverified"),
        new(
            "sit_display_hold_time",
            "SitReportList[].SitTime",
            "s",
            "confirmed",
            "SitTime"),
        new(
            "stand_display_hold_time",
            "SitReportList[].StandTime",
            "s",
            "confirmed",
            "StandTime"),
        new(
            "sit_stand_display_speed",
            "SitReportList[].Speed",
            "mm/s",
            "confirmed",
            "Speed"),
        new(
            "sit_stand_display_unloading",
            "SitReportList[].WeightHoss",
            "kg",
            "confirmed",
            "WeightHoss")
    };

    private static readonly IReadOnlyDictionary<int, MetricDefinition[]> MetricsByMode =
        new Dictionary<int, MetricDefinition[]>
        {
            [1] = WalkingMetrics,
            [2] = SitStandMetrics,
            [3] = new MetricDefinition[]
            {
                new("game_mode", "GameMode", null, "source_declared"),
                new("game_set_time", "GTime", "s", "confirmed"),
                new("game_difficulty", "GDifficulty", "level", "source_declared"),
                new("game_score", "GScore", "score", "source_declared"),
                new("game_training_duration", "GTrainingTime", "s", "confirmed"),
                new("game_star", "Star", "count", "source_declared")
            },
            [4] = new MetricDefinition[]
            {
                new("balance_mode", "BalanceMode", null, "source_declared"),
                new("balance_set_or_record_time", "BTime", "s", "confirmed"),
                new("balance_unloading", "BImponderability", "kg", "confirmed"),
                new("balance_training_duration", "RTrainingBalanceTime", "s", "confirmed"),
                new("balance_trajectory_length", "BTrajectoryLength", "mm", "confirmed"),
                new("balance_trajectory_area", "BTrajectoryArea", "mm2", "confirmed"),
                new("balance_front_back_average", "BAvgFrontAndBack", "mm", "confirmed"),
                new("balance_left_right_average", "BAvgLeftAndRight", "mm", "confirmed"),
                new("balance_max_front_lean", "BMaxFrontLean", "mm", "confirmed"),
                new("balance_max_back_lean", "BMaxBackLean", "mm", "confirmed"),
                new("balance_max_left_lean", "BMaxLeftLean", "mm", "confirmed"),
                new("balance_max_right_lean", "BMaxRightLean", "mm", "confirmed"),
                new("balance_average_amplitude_front_back", "AvgAmpFB", "mm", "confirmed"),
                new("balance_average_amplitude_left_right", "AvgAmpLR", "mm", "confirmed"),
                new("balance_max_amplitude_front_back", "MaxAmpFB", "mm", "confirmed"),
                new("balance_max_amplitude_left_right", "MaxAmpLR", "mm", "confirmed"),
                new("balance_average_speed_front_back", "AvgSpeedFB", "mm/s", "confirmed"),
                new("balance_average_speed_left_right", "AvgSpeedLR", "mm/s", "confirmed"),
                new("balance_data_area", "dataArea", "mm2", "confirmed"),
                new("vision_open_length", "cLBalanceVision.OpenLength", "mm", "confirmed", "OpenLength"),
                new("vision_close_length", "cLBalanceVision.CloseLength", "mm", "confirmed", "CloseLength"),
                new("vision_open_area", "cLBalanceVision.OpenArea", "mm2", "confirmed", "OpenArea"),
                new("vision_close_area", "cLBalanceVision.CloseArea", "mm2", "confirmed", "CloseArea"),
                new("romberg_area", "cLBalanceVision.RombergArea", "%", "confirmed", "RombergArea"),
                new("romberg_length", "cLBalanceVision.RombergLength", "%", "confirmed", "RombergLength"),
                new("vestibular_length", "cLBalanceVesti.VestiLength", "mm", "confirmed", "VestiLength"),
                new("vestibular_area", "cLBalanceVesti.VestiArea", "mm2", "confirmed", "VestiArea"),
                new("vestibular_average_speed", "cLBalanceVesti.VestiAvgSpeed", "mm/s", "confirmed", "VestiAvgSpeed"),
                new("vestibular_average_swing", "cLBalanceVesti.VestiAvgSwing", "mm", "confirmed", "VestiAvgSwing"),
                new("vestibular_max_swing_x", "cLBalanceVesti.VestiMaxSwingX", "mm", "confirmed", "VestiMaxSwingX"),
                new("vestibular_max_swing_y", "cLBalanceVesti.VestiMaxSwingY", "mm", "confirmed", "VestiMaxSwingY"),
                new("proprio_both_max_swing_x", "cLBalanceNou.NouBoothMaxSwingX", "mm", "confirmed", "NouBoothMaxSwingX"),
                new("proprio_both_max_swing_y", "cLBalanceNou.NouBoothMaxSwingY", "mm", "confirmed", "NouBoothMaxSwingY"),
                new("stability_field", "cLBalanceStab.StabilityField", "mm", "confirmed", "StabilityField")
            },
            [5] = WalkingMetrics,
            [6] = SitStandMetrics
        };

    public ParsedIReGoReport Parse(string? reportDetails)
    {
        if (string.IsNullOrWhiteSpace(reportDetails))
        {
            return Invalid(IReGoReasonCodes.ReportDetailsInvalid, "ReportDetails is empty.");
        }

        JObject root;
        try
        {
            root = JObject.Parse(reportDetails, new JsonLoadSettings
            {
                DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Replace
            });
        }
        catch (JsonException exception)
        {
            return Invalid(IReGoReasonCodes.ReportDetailsInvalid, exception.Message);
        }

        var signature = BuildSchemaSignature(root);
        var reportList = root.GetValue("ReportList", StringComparison.OrdinalIgnoreCase) as JArray;
        if (reportList == null)
        {
            return new ParsedIReGoReport
            {
                ParseSucceeded = true,
                HasReportList = false,
                SchemaFamily = "report_details_without_report_list",
                SchemaSignature = signature,
                Issues = new[]
                {
                    Issue(
                        IReGoReasonCodes.ReportListMissing,
                        "error",
                        "schema",
                        "block_session",
                        "ReportDetails.ReportList is absent or is not an array.")
                }
            };
        }

        var issues = new List<ToolIssueDto>();
        var direct = new List<IReGoContentBlockDto>();
        var games = new List<IReGoContentBlockDto>();
        var assessments = new List<IReGoContentBlockDto>();
        var modes = new List<int>();
        var validMetricCount = 0;
        var excludedMetricCount = 0;
        var sequence = 0;
        var blockOrdinals = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var token in reportList)
        {
            sequence++;
            if (token is not JObject item)
            {
                issues.Add(Issue(
                    IReGoReasonCodes.ReportDetailsInvalid,
                    "warning",
                    $"report_list[{sequence - 1}]",
                    "exclude_item",
                    "ReportList item is not an object."));
                continue;
            }

            var reportMode = ReadInt(item, "ReportMode");
            var balanceMode = ReadInt(item, "BalanceMode") ?? ReadInt(item, "Balance_Mode");
            if (reportMode.HasValue)
            {
                modes.Add(reportMode.Value);
            }

            var classification = Classify(reportMode, balanceMode, item);
            if (!reportMode.HasValue || classification.RehabContent == "unknown" && reportMode != 3)
            {
                issues.Add(Issue(
                    IReGoReasonCodes.UnknownReportMode,
                    "warning",
                    $"report_list[{sequence - 1}]",
                    "retain_as_unknown",
                    $"ReportMode={reportMode?.ToString(CultureInfo.InvariantCulture) ?? "null"}"));
            }

            var metricFacts = RepairMetrics(
                ExtractMetrics(item, reportMode, issues),
                reportMode);
            ValidateWalkingMetricConsistency(
                classification.RehabContent,
                sequence,
                metricFacts,
                issues);
            validMetricCount += metricFacts.Count(x => x.ValueStatus == "valid");
            excludedMetricCount += metricFacts.Count(x => x.ValueStatus != "valid");
            var ordinalKey = classification.RehabContent;
            var blockOrdinal = blockOrdinals.TryGetValue(ordinalKey, out var current)
                ? current + 1
                : 1;
            blockOrdinals[ordinalKey] = blockOrdinal;
            var displayName = BuildDisplayName(
                classification.RehabContent,
                classification.InteractionType,
                classification.Name,
                blockOrdinal);

            var block = new IReGoContentBlockDto
            {
                Sequence = sequence,
                ReportMode = reportMode,
                BalanceMode = balanceMode,
                RehabContent = classification.RehabContent,
                InteractionType = classification.InteractionType,
                Name = displayName,
                DisplayName = displayName,
                TrainingModeName = ResolveTrainingModeName(
                    classification.RehabContent,
                    metricFacts),
                Metrics = metricFacts
            };

            if (classification.InteractionType == "assessment")
            {
                assessments.Add(block);
            }
            else if (classification.InteractionType is "game" or "vr_game")
            {
                games.Add(block);
            }
            else
            {
                direct.Add(block);
            }
        }

        ValidateReportProcess(root, modes, issues);

        var contentTypes = direct
            .Concat(games)
            .Concat(assessments)
            .Select(x => x.RehabContent)
            .Where(x => x != "unknown")
            .Distinct()
            .ToArray();

        return new ParsedIReGoReport
        {
            ParseSucceeded = true,
            HasReportList = true,
            SchemaFamily = "robotdb_reportlist_v1",
            SchemaSignature = signature,
            DirectTraining = direct,
            Games = games,
            Assessments = assessments,
            Issues = issues,
            ReportItemCount = reportList.Count,
            ReportBlockCount = reportList.Count,
            ValidMetricCount = validMetricCount,
            ExcludedMetricCount = excludedMetricCount,
            ContentTypes = contentTypes
        };
    }

    private static IReadOnlyList<MetricFactDto> ExtractMetrics(
        JObject item,
        int? reportMode,
        ICollection<ToolIssueDto> issues)
    {
        if (!reportMode.HasValue ||
            !MetricsByMode.TryGetValue(reportMode.Value, out var definitions))
        {
            return Array.Empty<MetricFactDto>();
        }

        var facts = new List<MetricFactDto>();
        var properties = item
            .DescendantsAndSelf()
            .OfType<JProperty>()
            .GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
        var emittedCodes = new HashSet<string>(StringComparer.Ordinal);

        foreach (var definition in definitions)
        {
            var lookupField = definition.LookupField ?? definition.SourceField;
            if (emittedCodes.Contains(definition.Code) ||
                !properties.TryGetValue(lookupField, out var property))
            {
                continue;
            }

            var raw = ToRawValue(property.Value);
            decimal? normalized;
            string status;
            if (definition.ValueKind is "duration_text" or "text")
            {
                var text = property.Value.Type == JTokenType.String
                    ? property.Value.Value<string>()
                    : property.Value.ToString();
                normalized = null;
                status = definition.ValueKind == "duration_text"
                    ? IsMinuteSecondText(text) ? "valid" : "invalid"
                    : string.IsNullOrWhiteSpace(text) ? "missing" : "valid";
            }
            else
            {
                normalized = ReadFiniteDecimal(property.Value, out status);
            }

            if (status == "non_finite")
            {
                issues.Add(Issue(
                    IReGoReasonCodes.NonFiniteMetric,
                    "warning",
                    $"metric.{definition.Code}",
                    "exclude_metric",
                    $"{definition.SourceField} contains NaN or Infinity."));
            }

            facts.Add(new MetricFactDto
            {
                MetricCode = definition.Code,
                RawValue = raw,
                NormalizedValue = normalized,
                ValueStatus = status,
                Unit = definition.Unit,
                UnitStatus = definition.UnitStatus,
                DataStage = "report_source",
                SourceField = definition.SourceField,
                DisplayName = IReGoMetricDisplayRegistry.Label(definition.Code),
                SourceKind = "raw",
                MetricEvidenceLevel = definition.UnitStatus == "confirmed"
                    ? "confirmed_source_fact"
                    : "source_fact",
                EvaluationStatus = definition.Code == "sit_stand_average_speed"
                    ? "paused_semantics_unresolved"
                    : "eligible_for_review"
            });
            emittedCodes.Add(definition.Code);
        }

        return facts;
    }

    private static IReadOnlyList<MetricFactDto> RepairMetrics(
        IReadOnlyList<MetricFactDto> source,
        int? reportMode)
    {
        var facts = source.ToList();
        if (reportMode is 1 or 5)
        {
            var duration = ValidMetric(facts, "walking_training_duration");
            var distance = ValidMetric(facts, "walking_distance");
            var rawSpeed = ValidMetric(facts, "walking_average_speed");
            if (duration > 0 && distance > 0 && (!rawSpeed.HasValue || rawSpeed <= 0))
            {
                MarkDefaultZeroUnusable(facts, "walking_average_speed");
                MarkDefaultZeroUnusable(facts, "walking_max_speed");
                MarkDefaultZeroUnusable(facts, "walking_step_count");
                facts.Add(new MetricFactDto
                {
                    MetricCode = "walking_average_speed_derived",
                    RawValue = null,
                    NormalizedValue = decimal.Round(
                        distance.Value / duration.Value,
                        4,
                        MidpointRounding.AwayFromZero),
                    ValueStatus = "valid",
                    Unit = "m/s",
                    UnitStatus = "confirmed",
                    DataStage = "report_derived",
                    SourceField =
                        "WTrainingWalkdistance/WTrainingWalktime",
                    DisplayName = "平均步行速度",
                    SourceKind = "derived",
                    MetricEvidenceLevel = "confirmed_formula",
                    EvaluationStatus = "eligible_for_review",
                    Repair = new IReGoMetricRepairEvidenceDto
                    {
                        Rule = "distance_m_div_duration_s",
                        RuleVersion = "irego-measurement-repair-1.0.0",
                        SourceFields = new[]
                        {
                            "WTrainingWalkdistance",
                            "WTrainingWalktime"
                        },
                        Confidence = "high"
                    }
                });
            }
        }

        if (reportMode == 4)
        {
            AddRombergDerived(
                facts,
                "vision_open_length",
                "vision_close_length",
                "romberg_length_recomputed",
                "Romberg轨迹长度比");
            AddRombergDerived(
                facts,
                "vision_open_area",
                "vision_close_area",
                "romberg_area_recomputed",
                "Romberg面积比");
        }
        if (reportMode == 3)
        {
            var gameDuration = ValidMetric(
                facts,
                "game_training_duration");
            var gameScore = ValidMetric(
                facts,
                "game_score");
            var status = !gameDuration.HasValue ||
                gameDuration <= 0 ||
                gameDuration < 1 ||
                gameDuration >= 99999
                    ? "paused_time_encoding_unresolved"
                    : "current_fact_only_scoring_version_missing";
            MarkEvaluationStatus(
                facts,
                "game_training_duration",
                status);
            MarkEvaluationStatus(
                facts,
                "game_score",
                "current_fact_only_scoring_version_missing");
            if (gameScore.HasValue &&
                gameDuration.HasValue &&
                gameDuration > 0 &&
                gameDuration < 99999)
            {
                var scoreRate = decimal.Round(
                    gameScore.Value / gameDuration.Value,
                    4,
                    MidpointRounding.AwayFromZero);
                facts.Add(new MetricFactDto
                {
                    MetricCode = "game_score_rate",
                    RawValue = scoreRate,
                    NormalizedValue = scoreRate,
                    ValueStatus = "valid",
                    Unit = "score/s",
                    UnitStatus = "confirmed",
                    DataStage = "report_derived",
                    SourceField = "GScore/GTrainingTime",
                    DisplayName = "单位时间得分",
                    SourceKind = "derived",
                    MetricEvidenceLevel = "confirmed_formula",
                    EvaluationStatus =
                        "descriptive_only_scoring_version_missing",
                    Repair = new IReGoMetricRepairEvidenceDto
                    {
                        Rule = "game_score_div_duration_s",
                        RuleVersion = "irego-measurement-repair-1.1.0",
                        SourceFields = new[] { "GScore", "GTrainingTime" },
                        Confidence = "high"
                    }
                });
            }
        }
        return facts;
    }

    private static void AddRombergDerived(
        ICollection<MetricFactDto> facts,
        string numeratorCode,
        string denominatorCode,
        string resultCode,
        string displayName)
    {
        var numerator = ValidMetric(facts.ToArray(), numeratorCode);
        var denominator = ValidMetric(facts.ToArray(), denominatorCode);
        if (!numerator.HasValue || !denominator.HasValue ||
            numerator <= 0 || denominator <= 0)
        {
            return;
        }
        facts.Add(new MetricFactDto
        {
            MetricCode = resultCode,
            NormalizedValue = decimal.Round(
                numerator.Value / denominator.Value * 100m,
                3,
                MidpointRounding.AwayFromZero),
            ValueStatus = "valid",
            Unit = "%",
            UnitStatus = "confirmed",
            DataStage = "report_derived",
            SourceField = $"{numeratorCode}/{denominatorCode}",
            DisplayName = displayName,
            SourceKind = "derived",
            MetricEvidenceLevel = "confirmed_formula",
            EvaluationStatus = "direction_only",
            Repair = new IReGoMetricRepairEvidenceDto
            {
                Rule = "open_over_close_percent",
                RuleVersion = "irego-measurement-repair-1.0.0",
                SourceFields = new[] { numeratorCode, denominatorCode },
                Confidence = "high"
            }
        });
    }

    private static void MarkDefaultZeroUnusable(
        IList<MetricFactDto> facts,
        string metricCode)
    {
        var index = facts.ToList().FindIndex(x =>
            x.MetricCode == metricCode &&
            x.NormalizedValue == 0);
        if (index < 0)
        {
            return;
        }
        var current = facts[index];
        facts[index] = new MetricFactDto
        {
            MetricCode = current.MetricCode,
            RawValue = current.RawValue,
            NormalizedValue = current.NormalizedValue,
            ValueStatus = "default_zero_unusable",
            Unit = current.Unit,
            UnitStatus = current.UnitStatus,
            DataStage = current.DataStage,
            SourceField = current.SourceField,
            DisplayName = current.DisplayName,
            SourceKind = current.SourceKind,
            MetricEvidenceLevel = "unusable_default_zero",
            EvaluationStatus = "paused_missing_measurement"
        };
    }

    private static void MarkEvaluationStatus(
        IList<MetricFactDto> facts,
        string metricCode,
        string evaluationStatus)
    {
        var index = facts.ToList().FindIndex(x =>
            x.MetricCode == metricCode);
        if (index < 0)
        {
            return;
        }
        var current = facts[index];
        facts[index] = new MetricFactDto
        {
            MetricCode = current.MetricCode,
            RawValue = current.RawValue,
            NormalizedValue = current.NormalizedValue,
            ValueStatus = current.ValueStatus,
            Unit = current.Unit,
            UnitStatus = current.UnitStatus,
            DataStage = current.DataStage,
            SourceField = current.SourceField,
            DisplayName = current.DisplayName,
            SourceKind = current.SourceKind,
            MetricEvidenceLevel = current.MetricEvidenceLevel,
            EvaluationStatus = evaluationStatus,
            Repair = current.Repair
        };
    }

    private static (string RehabContent, string InteractionType, string? Name) Classify(
        int? reportMode,
        int? balanceMode,
        JObject item)
    {
        return reportMode switch
        {
            1 => ("walking", "direct_training", null),
            2 => ("sit_stand", "direct_training", null),
            3 when ReadBool(item, "IsVR") == true => (
                "vr_game",
                "vr_game",
                ResolveGameName(item)),
            3 => (
                "game",
                "game",
                ResolveGameName(item)),
            4 when balanceMode is >= 5 and <= 8 =>
                ("balance_assessment", "assessment", null),
            4 => ("balance_training", "direct_training", null),
            5 => ("single_walking", "direct_training", null),
            6 => ("single_sit", "direct_training", null),
            _ => ("unknown", "direct_training", null)
        };
    }

    private static string BuildDisplayName(
        string rehabContent,
        string interactionType,
        string? sourceName,
        int ordinal)
    {
        if (interactionType is "game" or "vr_game" &&
            !string.IsNullOrWhiteSpace(sourceName))
        {
            return sourceName.Trim();
        }

        return rehabContent switch
        {
            "walking" => $"步行训练第{ordinal}段",
            "sit_stand" => $"坐站训练第{ordinal}段",
            "balance_training" => $"平衡训练第{ordinal}段",
            "balance_assessment" => $"平衡评估第{ordinal}段",
            "single_walking" => $"单侧步行训练第{ordinal}段",
            "single_sit" => $"单侧坐站训练第{ordinal}段",
            "game" or "vr_game" => $"训练游戏第{ordinal}段",
            _ => $"训练记录第{ordinal}段"
        };
    }

    private static string? ResolveGameName(JObject item)
    {
        var sourceName = ReadNonBlankString(item, "GameName");
        if (!string.IsNullOrWhiteSpace(sourceName))
        {
            return sourceName;
        }

        var gameMode = ReadInt(item, "GameMode");
        return IReGoGameModeRegistry.DisplayName(gameMode) ??
            (gameMode.HasValue ? $"GameMode {gameMode.Value}" : null);
    }

    private static string? ResolveTrainingModeName(
        string rehabContent,
        IReadOnlyList<MetricFactDto> metrics)
    {
        var modeCode = rehabContent is "walking" or "single_walking"
            ? "walking_mode"
            : rehabContent is "sit_stand" or "single_sit"
                ? "sit_stand_mode"
                : null;
        if (modeCode == null)
        {
            return null;
        }
        var mode = ValidMetric(metrics, modeCode);
        return (modeCode, mode) switch
        {
            ("walking_mode", 0m) => "跟随",
            ("walking_mode", 1m) => "助力",
            ("walking_mode", 2m) => "阻力",
            ("sit_stand_mode", 0m) => "主动",
            ("sit_stand_mode", 1m) => "被动",
            _ => null
        };
    }

    private static void ValidateWalkingMetricConsistency(
        string rehabContent,
        int sequence,
        IReadOnlyList<MetricFactDto> metrics,
        ICollection<ToolIssueDto> issues)
    {
        if (rehabContent != "walking")
        {
            return;
        }

        var distance = ValidMetric(metrics, "walking_distance");
        var steps = ValidMetric(metrics, "walking_step_count");
        var averageSpeed = ValidMetric(metrics, "walking_average_speed");
        if (distance > 0 && steps == 0 && averageSpeed == 0)
        {
            issues.Add(Issue(
                IReGoReasonCodes.WalkingDistanceWithoutStepsOrSpeed,
                "warning",
                $"report_list[{sequence - 1}]",
                "explain_metric_inconsistency",
                "Walking distance is positive while step count and average speed are both zero.",
                "这段步行记录有距离数据，但步数和平均速度均为零，相关指标可能不完整，不能直接判断为正常。"));
        }
    }

    private static decimal? ValidMetric(
        IReadOnlyList<MetricFactDto> metrics,
        string code) =>
        metrics.FirstOrDefault(x =>
            x.MetricCode == code &&
            x.ValueStatus == "valid")?.NormalizedValue;

    private static void ValidateReportProcess(
        JObject root,
        IReadOnlyList<int> modes,
        ICollection<ToolIssueDto> issues)
    {
        var process = ReadString(root, "ReportProcess");
        if (string.IsNullOrWhiteSpace(process) || modes.Count == 0)
        {
            return;
        }

        var expected = string.Concat(modes.Select(x => x.ToString(CultureInfo.InvariantCulture)));
        if (!string.Equals(process.Trim(), expected, StringComparison.Ordinal))
        {
            issues.Add(Issue(
                IReGoReasonCodes.ReportProcessMismatch,
                "warning",
                "report_process",
                "do_not_use_as_primary_content",
                $"ReportProcess={process}; ReportList sequence={expected}."));
        }
    }

    private static string BuildSchemaSignature(JObject root)
    {
        var descriptors = root
            .DescendantsAndSelf()
            .SelectMany(token => token is JObject obj
                ? obj.Properties().Select(property =>
                    $"{property.Path}:{property.Value.Type}")
                : Enumerable.Empty<string>())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal);
        var payload = string.Join("|", descriptors);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return $"rf_{Convert.ToHexString(hash)[..16].ToLowerInvariant()}";
    }

    private static decimal? ReadFiniteDecimal(JToken token, out string status)
    {
        if (token.Type is JTokenType.Float or JTokenType.Integer)
        {
            var number = token.Value<double>();
            if (double.IsNaN(number) || double.IsInfinity(number))
            {
                status = "non_finite";
                return null;
            }

            try
            {
                status = "valid";
                return Convert.ToDecimal(number, CultureInfo.InvariantCulture);
            }
            catch (OverflowException)
            {
                status = "out_of_range";
                return null;
            }
        }

        var text = token.Type == JTokenType.String ? token.Value<string>() : null;
        if (text is not null &&
            (text.Equals("NaN", StringComparison.OrdinalIgnoreCase) ||
             text.Equals("Infinity", StringComparison.OrdinalIgnoreCase) ||
             text.Equals("-Infinity", StringComparison.OrdinalIgnoreCase)))
        {
            status = "non_finite";
            return null;
        }

        if (decimal.TryParse(
                text,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var parsed))
        {
            status = "valid";
            return parsed;
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

    private static bool IsMinuteSecondText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var parts = value.Trim().Split(':');
        return parts.Length == 2 &&
               int.TryParse(
                   parts[0],
                   NumberStyles.None,
                   CultureInfo.InvariantCulture,
                   out var minutes) &&
               int.TryParse(
                   parts[1],
                   NumberStyles.None,
                   CultureInfo.InvariantCulture,
                   out var seconds) &&
               minutes >= 0 &&
               seconds is >= 0 and <= 59;
    }

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
        return token == null || token.Type is JTokenType.Null or JTokenType.Undefined
            ? null
            : token.ToString();
    }

    private static string? ReadNonBlankString(JObject obj, string name)
    {
        var value = ReadString(obj, name);
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static ParsedIReGoReport Invalid(string code, string detail) => new()
    {
        ParseSucceeded = false,
        Issues = new[] { Issue(code, "error", "schema", "block_session", detail) }
    };

    private static ToolIssueDto Issue(
        string code,
        string severity,
        string scope,
        string action,
        string? detail = null,
        string? patientMessage = null) => new()
        {
            Code = code,
            Severity = severity,
            Scope = scope,
            Action = action,
            Detail = detail,
            PatientMessage = patientMessage
        };

    private sealed record MetricDefinition(
        string Code,
        string SourceField,
        string? Unit,
        string UnitStatus,
        string? LookupField = null,
        string ValueKind = "numeric");
}
