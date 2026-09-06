using System.Globalization;
using AI_WebApi.IServices.AITools;
using AI_WebApi.Model.Domain.IReGo;
using AI_WebApi.Model.DTOs.AITools;

namespace AI_WebApi.Services.AITools.IReGo;

/// <summary>
/// Versioned read-only threshold registry. Database exploratory MDC values are
/// used only for their exact condition profile. When an internal value differs
/// from the nearest external value by more than 50%, or no usable internal
/// value exists, the external MDC lower bound is selected. The device
/// observation floor is then applied without rewriting the source MDC.
/// </summary>
public sealed class IReGoThresholdRegistry : IIReGoThresholdRegistry
{
    public const decimal MaximumExternalRelativeDifference = 0.50m;
    public string Version => "irego-threshold-registry-1.1.0";

    private static readonly IReadOnlyDictionary<string, ThresholdProfile>
        Profiles = BuildProfiles();

    public IReGoMetricThresholdDto? Resolve(
        string metricCode,
        string? unit,
        IReadOnlyDictionary<string, decimal?> conditions)
    {
        if (!Profiles.TryGetValue(metricCode, out var profile) ||
            string.IsNullOrWhiteSpace(unit) ||
            !string.Equals(profile.Unit, unit, StringComparison.Ordinal))
        {
            return null;
        }

        var internalCandidate = profile.InternalCandidates
            .FirstOrDefault(x => Matches(x.RequiredConditions, conditions));
        var externalValues = profile.ExternalValues
            .Where(x => x > 0)
            .OrderBy(x => x)
            .ToArray();
        var externalLower = externalValues.Length == 0
            ? (decimal?)null
            : externalValues[0];
        decimal? relativeDifference = null;
        var useInternal = internalCandidate != null;
        if (internalCandidate != null && externalValues.Length > 0)
        {
            var nearest = externalValues
                .OrderBy(x => Math.Abs(x - internalCandidate.Value))
                .First();
            relativeDifference = decimal.Round(
                Math.Abs(internalCandidate.Value - nearest) / nearest,
                4,
                MidpointRounding.AwayFromZero);
            useInternal = relativeDifference <=
                MaximumExternalRelativeDifference;
        }

        decimal selected;
        string source;
        string evidenceLevel;
        string policy;
        string? evidenceReference;
        if (useInternal && internalCandidate != null)
        {
            selected = internalCandidate.Value;
            source = "database_exploratory";
            evidenceLevel = "B";
            policy = externalLower.HasValue
                ? "database_value_within_50_percent_of_nearest_external"
                : "database_value_no_protocol_matched_external_available";
            evidenceReference = internalCandidate.EvidenceReference;
        }
        else if (externalLower.HasValue)
        {
            selected = externalLower.Value;
            source = internalCandidate == null
                ? "external_mdc_lower_bound"
                : "external_mdc_lower_bound_internal_difference_exceeded";
            evidenceLevel = "C";
            policy = internalCandidate == null
                ? "unrepaired_or_no_usable_internal_value_use_external_lower_bound"
                : "internal_external_relative_difference_above_50_percent_use_external_lower_bound";
            evidenceReference = profile.ExternalEvidenceReference;
        }
        else
        {
            return null;
        }

        var effective = Math.Max(selected, profile.ObservableChangeFloor);
        if (effective > selected)
        {
            policy += "_then_apply_observable_change_floor";
        }

        return new IReGoMetricThresholdDto
        {
            Value = effective,
            Unit = profile.Unit,
            Source = source,
            EvidenceLevel = evidenceLevel,
            IsIReGoMdc = false,
            ProductionUsable = true,
            DatabaseExploratoryValue = internalCandidate?.Value,
            ExternalLowerBound = externalLower,
            ObservableChangeFloor = profile.ObservableChangeFloor,
            RelativeDifferenceFromExternal = relativeDifference,
            SelectionPolicy = policy,
            EvidenceReference = evidenceReference,
            RegistryVersion = Version
        };
    }

    private static bool Matches(
        IReadOnlyDictionary<string, decimal> required,
        IReadOnlyDictionary<string, decimal?> actual) =>
        required.All(pair =>
            actual.TryGetValue(pair.Key, out var value) &&
            value.HasValue &&
            Math.Abs(value.Value - pair.Value) <= 0.001m);

    private static IReadOnlyDictionary<string, ThresholdProfile>
        BuildProfiles()
    {
        const string wearable =
            "https://pmc.ncbi.nlm.nih.gov/articles/PMC9862973/";
        const string tenMetre =
            "https://pmc.ncbi.nlm.nih.gov/articles/PMC10395330/";
        const string optoGait =
            "https://pmc.ncbi.nlm.nih.gov/articles/PMC4106927/";
        const string balance =
            "https://pmc.ncbi.nlm.nih.gov/articles/PMC3999988/";
        const string databaseEvidence =
            "irego_database_exploratory_2026-08-15";

        return new Dictionary<string, ThresholdProfile>(StringComparer.Ordinal)
        {
            ["walking_average_speed_derived"] = new(
                "m/s", 0.001m, new[] { 0.05m, 0.11m, 0.21m }, tenMetre),
            ["walking_average_speed"] = new(
                "m/s", 0.001m, new[] { 0.05m, 0.11m, 0.21m }, tenMetre),
            ["walking_step_frequency"] = new(
                "step/min", 0.01m, new[] { 0.95m }, optoGait),
            ["walking_average_gait_period"] = new(
                "s", 1m, new[] { 0.0157m }, optoGait),
            ["walking_average_stride"] = new(
                "m", 0.001m, new[] { 0.007m }, optoGait),
            ["walking_step_time"] = new(
                "s", 1m, new[] { 0.016m, 0.020m }, wearable),
            ["walking_step_length"] = new(
                "m",
                0.001m,
                new[] { 0.0024m, 0.0105m, 0.0528m, 0.2064m },
                wearable,
                new[]
                {
                    Candidate(
                        0.25045m,
                        databaseEvidence,
                        ("walking_mode", 2m),
                        ("unloading", 30m),
                        ("force", 24m))
                }),
            ["walking_step_width"] = new(
                "mm",
                1m,
                new[] { 38.1m, 50.3m },
                wearable,
                new[]
                {
                    Candidate(
                        31.49m,
                        databaseEvidence,
                        ("walking_mode", 0m),
                        ("unloading", 0m),
                        ("force", 0m))
                }),
            ["sit_stand_cycle_maximum"] = new(
                "s",
                1m,
                Array.Empty<decimal>(),
                null,
                new[]
                {
                    Candidate(
                        14.90m,
                        databaseEvidence,
                        ("sit_stand_mode", 2m),
                        ("unloading", 10m))
                }),
            ["proprio_both_max_swing_x"] = new(
                "mm",
                1m,
                Array.Empty<decimal>(),
                null,
                new[]
                {
                    Candidate(
                        28.79m,
                        databaseEvidence,
                        ("balance_mode", 7m))
                }),
            ["balance_average_speed_front_back"] = new(
                "mm/s", 1m, new[] { 4.9m, 7.3m }, balance),
            ["balance_average_speed_left_right"] = new(
                "mm/s", 1m, new[] { 3.6m, 5.5m }, balance),
            ["vestibular_average_speed"] = new(
                "mm/s", 1m, new[] { 6.1m, 9.5m }, balance)
        };
    }

    private static InternalCandidate Candidate(
        decimal value,
        string evidenceReference,
        params (string Key, decimal Value)[] conditions) =>
        new(
            value,
            conditions.ToDictionary(x => x.Key, x => x.Value),
            evidenceReference);

    private sealed record ThresholdProfile(
        string Unit,
        decimal ObservableChangeFloor,
        IReadOnlyList<decimal> ExternalValues,
        string? ExternalEvidenceReference,
        IReadOnlyList<InternalCandidate> InternalCandidates)
    {
        public ThresholdProfile(
            string unit,
            decimal observableChangeFloor,
            IReadOnlyList<decimal> externalValues,
            string? externalEvidenceReference)
            : this(
                unit,
                observableChangeFloor,
                externalValues,
                externalEvidenceReference,
                Array.Empty<InternalCandidate>())
        {
        }
    }

    private sealed record InternalCandidate(
        decimal Value,
        IReadOnlyDictionary<string, decimal> RequiredConditions,
        string EvidenceReference);
}

public sealed class IReGoStrokeCapabilityMapper : IIReGoStrokeCapabilityMapper
{
    public string Version => "irego-stroke-capability-map-1.2.0";

    private static readonly IReadOnlyDictionary<string, IReGoCapabilityDefinition>
        Map = BuildMap();

    public IReGoCapabilityDefinition? Resolve(string metricCode) =>
        Map.TryGetValue(metricCode, out var definition) ? definition : null;

    private static IReadOnlyDictionary<string, IReGoCapabilityDefinition>
        BuildMap()
    {
        var map = new Dictionary<string, IReGoCapabilityDefinition>(
            StringComparer.Ordinal);
        Add(map, "walking_ability", "步行能力", "higher_better",
            "walking_average_speed", "walking_average_speed_derived");
        Add(map, "walking_rhythm", "行走节奏能力", "descriptive_only",
            "walking_step_frequency", "walking_average_gait_period",
            "walking_step_time");
        Add(map, "stepping_control", "迈步控制能力", "descriptive_only",
            "walking_step_length", "walking_step_width",
            "walking_average_stride");
        Add(map, "sit_stand_ability", "坐站能力", "lower_better",
            "sit_stand_cycle_average", "sit_stand_cycle_maximum",
            "sit_stand_cycle_minimum");
        Add(map, "balance_ability", "平衡能力", "lower_better",
            "balance_average_speed_front_back",
            "balance_average_speed_left_right",
            "vestibular_average_speed", "balance_trajectory_length",
            "balance_trajectory_area", "vestibular_length",
            "vestibular_area");
        Add(map, "weight_shift_ability", "重心移动能力", "higher_better",
            "stability_field");
        Add(map, "game_training_performance", "游戏任务表现", "higher_better",
            "game_score_rate");
        return map;
    }

    private static void Add(
        IDictionary<string, IReGoCapabilityDefinition> map,
        string code,
        string name,
        string direction,
        params string[] metrics)
    {
        foreach (var metric in metrics)
        {
            map[metric] = new IReGoCapabilityDefinition
            {
                Code = code,
                Name = name,
                BetterDirection = direction
            };
        }
    }
}

public sealed class IReGoPatientMessageComposer : IIReGoPatientMessageComposer
{
    public string Version => "irego-patient-message-templates-1.3.0";

    public string ComposeMetric(
        string verdict,
        string metricLabel,
        IReGoCapabilityDefinition capability,
        string betterDirection)
    {
        var betterChange = betterDirection == "lower_better"
            ? LowerBetterVerb(metricLabel)
            : "连续提高";
        var worseChange = betterDirection == "lower_better"
            ? LowerWorseVerb(metricLabel)
            : "连续下降";
        return verdict switch
        {
            "improved" =>
                $"您的{metricLabel}{betterChange}，{capability.Name}改善。",
            "stable" =>
                $"您的{metricLabel}变化不大，{capability.Name}无明显变化。",
            "worsened" =>
                $"您的{metricLabel}{worseChange}，{capability.Name}表现下降。",
            "mixed" => $"您的{capability.Name}表现有好有坏。",
            _ => $"{capability.Name}的可比记录不足，暂时无法评价。"
        };
    }

    public string ComposeCapability(
        string verdict,
        IReGoCapabilityDefinition capability) => verdict switch
        {
            "improved" => $"您的{capability.Name}改善。",
            "stable" => $"您的{capability.Name}无明显变化。",
            "worsened" => $"您的{capability.Name}表现下降。",
            "mixed" => $"您的{capability.Name}表现有好有坏。",
            _ => $"{capability.Name}的可比记录不足，暂时无法评价。"
        };

    public string ComposeMetricExplanation(
        string verdict,
        string metricLabel,
        decimal baseline,
        decimal current,
        decimal delta,
        IReGoMetricThresholdDto threshold)
    {
        var unit = string.IsNullOrWhiteSpace(threshold.Unit)
            ? string.Empty
            : $" {threshold.Unit}";
        var thresholdSource = threshold.Source.StartsWith(
                "external_",
                StringComparison.Ordinal)
            ? "外部参考下限"
            : threshold.Source == "database_exploratory"
                ? "当前同条件数据阈值"
                : "当前规则阈值";
        var reason = verdict switch
        {
            "improved" => "变化达到评价阈值，方向与能力改善一致",
            "worsened" => "变化达到评价阈值，方向与能力下降一致",
            "stable" => "变化没有达到评价阈值",
            "mixed" => "各次记录出现达到阈值的相反方向变化",
            _ => "可比记录不足"
        };
        return $"在同一训练条件下，{metricLabel}由{Number(baseline)}{unit}变为{Number(current)}{unit}，变化{Signed(delta)}{unit}；{thresholdSource}为{Number(threshold.Value)}{unit}，{reason}。";
    }

    public string ComposeCapabilityExplanation(
        string verdict,
        IReGoCapabilityDefinition capability,
        IReadOnlyList<string> evidenceExplanations)
    {
        var evidence = evidenceExplanations
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal)
            .Take(2)
            .ToArray();
        if (evidence.Length == 0)
        {
            return verdict == "insufficient_data"
                ? $"{capability.Name}需要至少3条同项目、同分段、同条件的有效记录；当前记录不足。"
                : $"{capability.Name}依据同条件训练记录进行评价。";
        }

        var prefix = verdict == "mixed"
            ? "相关记录的变化方向不一致。"
            : string.Empty;
        return prefix + string.Join(string.Empty, evidence);
    }

    public string ComposeLifeHint(
        string verdict,
        IReGoCapabilityDefinition capability)
    {
        var activity = LifeActivity(capability.Code);
        return ComposeActivityHint(verdict, activity);
    }

    public IReGoLayeredPatientMessage ComposeGame(
        string verdict,
        string comparisonMode,
        string displayName,
        int baselineDifficulty,
        int currentDifficulty,
        decimal baselineRate,
        decimal currentRate,
        decimal threshold,
        bool currentComplete)
    {
        var sameDifficulty = comparisonMode == "same_difficulty";
        string basic;
        if (sameDifficulty)
        {
            basic = verdict switch
            {
                "improved" => "在相同难度下，您的单位时间得分持续提高，游戏任务表现改善。",
                "stable" => "在相同难度下，您的单位时间得分变化不大，游戏任务表现无明显变化。",
                "worsened" => "在相同难度下，您的单位时间得分持续下降，游戏任务表现下降。",
                "mixed" => "在相同难度下，您的单位时间得分有升有降，游戏任务表现有好有坏。",
                _ => "同一难度下的可比游戏记录不足，暂时无法评价。"
            };
        }
        else if (!currentComplete)
        {
            basic = $"游戏难度由{baselineDifficulty}调整到{currentDifficulty}，但本次没有完整完成，游戏任务表现有好有坏。";
        }
        else if (currentDifficulty > baselineDifficulty)
        {
            basic = verdict == "improved"
                ? "游戏难度提高后，您仍完整完成并保持了单位时间得分，游戏任务表现改善。"
                : "游戏难度提高并完整完成，但单位时间得分有所下降，游戏任务表现有好有坏。";
        }
        else
        {
            basic = verdict == "worsened"
                ? "游戏难度降低后，单位时间得分仍明显下降，游戏任务表现下降。"
                : "游戏难度降低后单位时间得分有所变化，但训练条件更容易，游戏任务表现有好有坏。";
        }

        var extended = sameDifficulty
            ? $"在相同难度{currentDifficulty}下，{displayName}的单位时间得分由{Number(baselineRate)} score/s变为{Number(currentRate)} score/s。游戏规则采用基线值的10%与0.01 score/s中的较大值作为变化阈值，本次阈值为{Number(threshold)} score/s；这是产品内的训练表现规则，不是脑卒中临床量表阈值。"
            : $"{displayName}由难度{baselineDifficulty}调整到难度{currentDifficulty}，单位时间得分由{Number(baselineRate)} score/s变为{Number(currentRate)} score/s。难度提高时，完整完成且得分效率不低于原难度的90%才判为改善；难度降低时，即使得分提高也不单独判为改善。";
        if (!currentComplete)
        {
            extended = $"{displayName}由难度{baselineDifficulty}调整到难度{currentDifficulty}，但最新一次没有完整完成，因此不判为改善或下降，只记录为表现有好有坏。";
        }

        return new IReGoLayeredPatientMessage
        {
            BasicEvaluation = basic,
            ExtendedExplanation = extended,
            LifeHint = ComposeActivityHint(
                verdict,
                LifeActivity("game_training_performance"))
        };
    }

    private static string ComposeActivityHint(
        string verdict,
        string activity) => verdict switch
        {
            "improved" =>
                $"这项训练与{activity}有关；当前相关训练表现向更顺畅的方向变化。",
            "stable" =>
                $"这项训练与{activity}有关；当前相关训练表现无明显变化。",
            "worsened" =>
                $"这项训练与{activity}有关；近期相关训练表现有所下降。",
            "mixed" =>
                $"这项训练与{activity}有关；近期相关训练表现有好有坏。",
            _ =>
                $"这项训练与{activity}有关；当前可比较记录不足。"
        };

    private static string LowerBetterVerb(string metricLabel) =>
        metricLabel.Contains("时间", StringComparison.Ordinal) ||
        metricLabel.Contains("周期", StringComparison.Ordinal)
            ? "连续缩短"
            : "连续减小";

    private static string LowerWorseVerb(string metricLabel) =>
        metricLabel.Contains("时间", StringComparison.Ordinal) ||
        metricLabel.Contains("周期", StringComparison.Ordinal)
            ? "连续延长"
            : "连续增大";

    private static string LifeActivity(string capabilityCode) =>
        capabilityCode switch
        {
            "walking_ability" or "walking_rhythm" or "stepping_control" =>
                "室内行走、短距离移动和转弯",
            "sit_stand_ability" or "active_sit_stand_participation" =>
                "从椅子起身以及床椅转移",
            "balance_ability" => "站立、转身和站立取物",
            "weight_shift_ability" => "左右移动重心、迈步和转身",
            "active_walking_participation" or "walking_load_tolerance" =>
                "在既定辅助条件下完成步行训练",
            "game_training_performance" =>
                "持续注意、及时反应和手脚配合完成任务",
            _ => "相关日常活动"
        };

    private static string Number(decimal value) => value.ToString(
        "0.####",
        CultureInfo.InvariantCulture);

    private static string Signed(decimal value) =>
        value > 0 ? "+" + Number(value) : Number(value);
}

public sealed class IReGoEvaluationRuleEngine : IIReGoEvaluationRuleEngine
{
    public const decimal GameRelativeChangeThreshold = 0.10m;
    public const decimal GameAbsoluteChangeFloor = 0.01m;
    public const decimal GameHarderDifficultyRetentionRatio = 0.90m;

    private readonly IIReGoThresholdRegistry _thresholdRegistry;
    private readonly IIReGoStrokeCapabilityMapper _capabilityMapper;
    private readonly IIReGoPatientMessageComposer _messageComposer;

    public IReGoEvaluationRuleEngine(
        IIReGoThresholdRegistry thresholdRegistry,
        IIReGoStrokeCapabilityMapper capabilityMapper,
        IIReGoPatientMessageComposer messageComposer)
    {
        _thresholdRegistry = thresholdRegistry;
        _capabilityMapper = capabilityMapper;
        _messageComposer = messageComposer;
    }

    public string Version => "irego-stroke-evaluation-rules-1.3.0";

    public IReGoLongitudinalInterpretationDto EvaluateMetric(
        IReGoMetricEvaluationInput input)
    {
        var capability = _capabilityMapper.Resolve(input.MetricCode);
        if (capability == null || input.Values.Count < 3)
        {
            return Unavailable(input, capability);
        }

        if (input.MetricCode == "game_score_rate")
        {
            return EvaluateSameDifficultyGame(input, capability);
        }

        var threshold = _thresholdRegistry.Resolve(
            input.MetricCode,
            input.Unit,
            input.Conditions);
        if (threshold == null ||
            input.UnitStatus != "confirmed" ||
            capability.BetterDirection == "descriptive_only")
        {
            return Unavailable(input, capability);
        }

        var values = input.Values.ToArray();
        var baseline = values.Length >= 5
            ? Median(values.Take(2))
            : values[0];
        var current = values.Length >= 5
            ? Median(values.TakeLast(2))
            : values[^1];
        var delta = current - baseline;
        var thresholdValue = threshold.Value;
        var hasMaterialIncrease = false;
        var hasMaterialDecrease = false;
        for (var index = 1; index < values.Length; index++)
        {
            var step = values[index] - values[index - 1];
            hasMaterialIncrease |= step >= thresholdValue;
            hasMaterialDecrease |= step <= -thresholdValue;
        }

        string verdict;
        string direction;
        if (hasMaterialIncrease && hasMaterialDecrease)
        {
            verdict = "mixed";
            direction = "variable";
        }
        else if (Math.Abs(delta) < thresholdValue)
        {
            verdict = "stable";
            direction = "unchanged";
        }
        else
        {
            var slope = TheilSenSlope(values);
            if (Math.Sign(slope) != Math.Sign(delta))
            {
                verdict = "mixed";
                direction = "variable";
            }
            else
            {
                direction = delta > 0 ? "increased" : "decreased";
                var objectivelyBetter =
                    capability.BetterDirection == "higher_better"
                        ? delta > 0
                        : delta < 0;
                verdict = objectivelyBetter ? "improved" : "worsened";
            }
        }

        var message = _messageComposer.ComposeMetric(
            verdict,
            input.MetricLabel,
            capability,
            capability.BetterDirection);
        var explanation = _messageComposer.ComposeMetricExplanation(
            verdict,
            input.MetricLabel,
            baseline,
            current,
            delta,
            threshold);
        var lifeHint = _messageComposer.ComposeLifeHint(verdict, capability);
        return new IReGoLongitudinalInterpretationDto
        {
            MetricCode = input.MetricCode,
            Mode = "trend",
            Direction = direction,
            Text = message,
            Verdict = verdict,
            CapabilityCode = capability.Code,
            CapabilityName = capability.Name,
            Strength = threshold.Source.StartsWith(
                    "external_",
                    StringComparison.Ordinal)
                ? "external_reference"
                : "database_exploratory",
            Delta = decimal.Round(delta, 4, MidpointRounding.AwayFromZero),
            Threshold = threshold,
            BasicEvaluation = message,
            ExtendedExplanation = explanation,
            LifeHint = lifeHint,
            PatientMessage = message,
            MessageRuleVersion = _messageComposer.Version,
            RuleSetVersion = Version
        };
    }

    public IReadOnlyList<IReGoCapabilityEvaluationDto> AggregateCapabilities(
        IEnumerable<IReGoLongitudinalInterpretationDto> interpretations)
    {
        var eligible = interpretations
            .Where(x =>
                !string.IsNullOrWhiteSpace(x.CapabilityCode) &&
                x.Verdict != "insufficient_data")
            .GroupBy(x => x.CapabilityCode, StringComparer.Ordinal);
        var result = new List<IReGoCapabilityEvaluationDto>();
        foreach (var group in eligible)
        {
            var first = group.First();
            var definition = new IReGoCapabilityDefinition
            {
                Code = first.CapabilityCode,
                Name = first.CapabilityName
            };
            var verdicts = group.Select(x => x.Verdict)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            var verdict = verdicts.Contains("mixed") ||
                verdicts.Contains("improved") && verdicts.Contains("worsened")
                    ? "mixed"
                    : verdicts.Contains("improved")
                        ? "improved"
                        : verdicts.Contains("worsened")
                            ? "worsened"
                            : "stable";
            var basic = _messageComposer.ComposeCapability(
                verdict,
                definition);
            var explanation = _messageComposer.ComposeCapabilityExplanation(
                verdict,
                definition,
                group.Select(x => x.ExtendedExplanation).ToArray());
            result.Add(new IReGoCapabilityEvaluationDto
            {
                CapabilityCode = definition.Code,
                CapabilityName = definition.Name,
                Verdict = verdict,
                EvaluationAxis = "result_performance",
                Strength = group.Any(x =>
                    x.Strength == "external_reference")
                        ? "external_reference"
                        : "database_exploratory",
                BasicEvaluation = basic,
                ExtendedExplanation = explanation,
                LifeHint = _messageComposer.ComposeLifeHint(
                    verdict,
                    definition),
                PatientMessage = basic,
                EvidenceMetricCodes = group
                    .Select(x => x.MetricCode)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.Ordinal)
                    .ToArray(),
                MessageRuleVersion = _messageComposer.Version,
                RuleSetVersion = Version
            });
        }
        return result
            .OrderBy(x => x.CapabilityCode, StringComparer.Ordinal)
            .ToArray();
    }

    public IReGoCapabilityEvaluationDto? EvaluateTrainingMode(
        IReGoTrainingModeEvaluationInput input)
    {
        if (input.RehabContent is not
                ("walking" or "single_walking" or
                 "sit_stand" or "single_sit") ||
            input.Observations.Count < 2)
        {
            return null;
        }

        var observations = input.Observations
            .OrderBy(x => x.ReportOrdinal)
            .ToArray();
        var completed = observations
            .Where(x => x.IsComplete && x.ModeCode.HasValue)
            .ToArray();
        if (completed.Length == 0)
        {
            return null;
        }

        var first = completed[0];
        var last = completed[^1];
        var latest = observations[^1];
        var isWalking = input.RehabContent is
            "walking" or "single_walking";
        var capabilityCode = isWalking
            ? "active_walking_participation"
            : "active_sit_stand_participation";
        var capabilityName = isWalking
            ? "自主步行训练能力"
            : "自主坐站能力";

        if (!latest.IsComplete)
        {
            var message =
                "本次训练没有完整完成，暂时不能据此判断运动能力改善或下降。";
            var definition = new IReGoCapabilityDefinition
            {
                Code = capabilityCode,
                Name = capabilityName
            };
            return new IReGoCapabilityEvaluationDto
            {
                CapabilityCode = capabilityCode,
                CapabilityName = capabilityName,
                Verdict = "insufficient_data",
                EvaluationAxis = "execution",
                Strength = "rule_based",
                BasicEvaluation = message,
                ExtendedExplanation =
                    "训练模式变化只有在相应训练完整完成时才用于评价；最新一次未完整完成，因此本项不下改善或下降结论。",
                LifeHint = _messageComposer.ComposeLifeHint(
                    "insufficient_data",
                    definition),
                PatientMessage = message,
                EvidenceMetricCodes = new[]
                {
                    isWalking ? "walking_mode" : "sit_stand_mode"
                },
                MessageRuleVersion = _messageComposer.Version,
                RuleSetVersion = Version
            };
        }

        var unloadingIncreased = first.Unloading.HasValue &&
            last.Unloading.HasValue &&
            last.Unloading.Value > first.Unloading.Value;
        var unloadingDecreased = first.Unloading.HasValue &&
            last.Unloading.HasValue &&
            last.Unloading.Value < first.Unloading.Value;
        if (isWalking && last.ModeCode == 2 && first.ModeCode != 2 &&
            !unloadingIncreased)
        {
            return ModeEvaluation(
                "walking_load_tolerance",
                "步行负荷耐受能力",
                "improved",
                "challenge",
                "您在阻力模式下完整完成训练，步行负荷耐受能力改善。",
                "walking_mode",
                "walking_unloading");
        }

        var participationImproved = isWalking
            ? first.ModeCode == 0 && last.ModeCode == 1
            : first.ModeCode == 1 && last.ModeCode == 0;
        if ((participationImproved || unloadingDecreased) &&
            !unloadingIncreased)
        {
            var message = isWalking
                ? "您在减少设备支持后仍完整完成训练，自主步行训练能力改善。"
                : participationImproved
                    ? "您从被动训练过渡到主动训练并完整完成，自主坐站能力改善。"
                    : "您在更少减重支持下仍完整完成坐站训练，自主坐站能力改善。";
            return ModeEvaluation(
                capabilityCode,
                capabilityName,
                "improved",
                "support_dependency",
                message,
                isWalking ? "walking_mode" : "sit_stand_mode",
                isWalking ? "walking_unloading" : "sit_stand_unloading");
        }

        var participationReduced = isWalking
            ? (first.ModeCode is 1 or 2) && last.ModeCode == 0
            : first.ModeCode == 0 && last.ModeCode == 1;
        if (participationReduced || unloadingIncreased)
        {
            return ModeEvaluation(
                capabilityCode,
                capabilityName,
                "mixed",
                "support_dependency",
                $"您近期需要更多设备支持，{capabilityName}表现有好有坏。",
                isWalking ? "walking_mode" : "sit_stand_mode",
                isWalking ? "walking_unloading" : "sit_stand_unloading");
        }

        if (completed.Length >= 3 &&
            first.ModeCode == last.ModeCode &&
            first.Unloading == last.Unloading)
        {
            return ModeEvaluation(
                capabilityCode,
                capabilityName,
                "stable",
                "support_dependency",
                $"您的{capabilityName}无明显变化。",
                isWalking ? "walking_mode" : "sit_stand_mode",
                isWalking ? "walking_unloading" : "sit_stand_unloading");
        }

        return null;
    }

    public IReGoCapabilityEvaluationDto? EvaluateGamePerformance(
        IReGoGamePerformanceEvaluationInput input)
    {
        var observations = input.Observations
            .Where(x => x.Difficulty.HasValue)
            .OrderBy(x => x.ReportOrdinal)
            .ToArray();
        if (observations.Length < 3)
        {
            return null;
        }

        var latest = observations[^1];
        var latestDifficulty = latest.Difficulty!.Value;
        var latestDifficultyRun = observations
            .Reverse()
            .TakeWhile(x => x.Difficulty == latestDifficulty)
            .Reverse()
            .ToArray();
        if (latestDifficultyRun.Count(x =>
                x.IsComplete && x.ScoreRate.HasValue) >= 3)
        {
            // The metric-series path owns same-difficulty evaluation.
            return null;
        }

        var priorSearchStart =
            observations.Length - latestDifficultyRun.Length - 1;
        if (priorSearchStart < 0)
        {
            return null;
        }
        var priorIndex = Array.FindLastIndex(
            observations,
            priorSearchStart,
            x => x.Difficulty.HasValue &&
                 x.Difficulty.Value != latestDifficulty &&
                 x.IsComplete &&
                 x.ScoreRate.HasValue);
        if (priorIndex < 0)
        {
            return null;
        }

        var baselineDifficulty = observations[priorIndex].Difficulty!.Value;
        var baselineRates = observations
            .Take(priorIndex + 1)
            .Reverse()
            .TakeWhile(x => x.Difficulty == baselineDifficulty)
            .Where(x => x.IsComplete && x.ScoreRate.HasValue)
            .Take(2)
            .Select(x => x.ScoreRate!.Value)
            .ToArray();
        if (baselineRates.Length == 0)
        {
            return null;
        }

        var currentRates = latestDifficultyRun
            .Where(x => x.IsComplete && x.ScoreRate.HasValue)
            .TakeLast(2)
            .Select(x => x.ScoreRate!.Value)
            .ToArray();
        if (latest.IsComplete && currentRates.Length == 0)
        {
            return null;
        }

        var baseline = Median(baselineRates);
        var current = currentRates.Length == 0
            ? baseline
            : Median(currentRates);
        var threshold = GameThreshold(baseline);
        string verdict;
        if (!latest.IsComplete)
        {
            verdict = "mixed";
        }
        else if (latestDifficulty > baselineDifficulty)
        {
            verdict = current >= baseline * GameHarderDifficultyRetentionRatio
                ? "improved"
                : "mixed";
        }
        else
        {
            verdict = current < baseline - threshold
                ? "worsened"
                : "mixed";
        }

        var copy = _messageComposer.ComposeGame(
            verdict,
            "cross_difficulty",
            input.DisplayName,
            baselineDifficulty,
            latestDifficulty,
            baseline,
            current,
            threshold,
            latest.IsComplete);
        return new IReGoCapabilityEvaluationDto
        {
            CapabilityCode = "game_training_performance",
            CapabilityName = "游戏任务表现",
            Verdict = verdict,
            EvaluationAxis = "cross_difficulty_score_rate",
            Strength = "rule_based_product_threshold",
            BasicEvaluation = copy.BasicEvaluation,
            ExtendedExplanation = copy.ExtendedExplanation,
            LifeHint = copy.LifeHint,
            PatientMessage = copy.BasicEvaluation,
            EvidenceMetricCodes = new[]
            {
                "game_score_rate",
                "game_difficulty"
            },
            MessageRuleVersion = _messageComposer.Version,
            RuleSetVersion = Version
        };
    }

    private IReGoLongitudinalInterpretationDto EvaluateSameDifficultyGame(
        IReGoMetricEvaluationInput input,
        IReGoCapabilityDefinition capability)
    {
        if (!input.Conditions.TryGetValue(
                "game_difficulty",
                out var difficultyValue) ||
            !difficultyValue.HasValue ||
            difficultyValue.Value != decimal.Truncate(difficultyValue.Value))
        {
            return Unavailable(input, capability);
        }

        var values = input.Values.ToArray();
        var baseline = values.Length >= 5
            ? Median(values.Take(2))
            : values[0];
        var current = values.Length >= 5
            ? Median(values.TakeLast(2))
            : values[^1];
        var delta = current - baseline;
        var thresholdValue = GameThreshold(baseline);
        var hasMaterialIncrease = false;
        var hasMaterialDecrease = false;
        for (var index = 1; index < values.Length; index++)
        {
            var step = values[index] - values[index - 1];
            hasMaterialIncrease |= step >= thresholdValue;
            hasMaterialDecrease |= step <= -thresholdValue;
        }

        string verdict;
        string direction;
        if (hasMaterialIncrease && hasMaterialDecrease)
        {
            verdict = "mixed";
            direction = "variable";
        }
        else if (Math.Abs(delta) < thresholdValue)
        {
            verdict = "stable";
            direction = "unchanged";
        }
        else
        {
            var slope = TheilSenSlope(values);
            if (Math.Sign(slope) != Math.Sign(delta))
            {
                verdict = "mixed";
                direction = "variable";
            }
            else
            {
                direction = delta > 0 ? "increased" : "decreased";
                verdict = delta > 0 ? "improved" : "worsened";
            }
        }

        var difficulty = (int)difficultyValue.Value;
        var threshold = new IReGoMetricThresholdDto
        {
            Value = thresholdValue,
            Unit = "score/s",
            Source = "irego_relative_product_rule",
            EvidenceLevel = "product_rule",
            IsIReGoMdc = false,
            ProductionUsable = true,
            ObservableChangeFloor = GameAbsoluteChangeFloor,
            SelectionPolicy =
                "max_of_baseline_10_percent_and_0.01_score_per_second",
            RegistryVersion = Version
        };
        var copy = _messageComposer.ComposeGame(
            verdict,
            "same_difficulty",
            "训练游戏",
            difficulty,
            difficulty,
            baseline,
            current,
            thresholdValue,
            true);
        return new IReGoLongitudinalInterpretationDto
        {
            MetricCode = input.MetricCode,
            Mode = "trend",
            Direction = direction,
            Text = copy.BasicEvaluation,
            Verdict = verdict,
            CapabilityCode = capability.Code,
            CapabilityName = capability.Name,
            Strength = "rule_based_product_threshold",
            Delta = decimal.Round(delta, 4, MidpointRounding.AwayFromZero),
            Threshold = threshold,
            BasicEvaluation = copy.BasicEvaluation,
            ExtendedExplanation = copy.ExtendedExplanation,
            LifeHint = copy.LifeHint,
            PatientMessage = copy.BasicEvaluation,
            MessageRuleVersion = _messageComposer.Version,
            RuleSetVersion = Version
        };
    }

    private IReGoCapabilityEvaluationDto ModeEvaluation(
        string capabilityCode,
        string capabilityName,
        string verdict,
        string axis,
        string message,
        params string[] evidenceCodes)
    {
        var definition = new IReGoCapabilityDefinition
        {
            Code = capabilityCode,
            Name = capabilityName
        };
        return new IReGoCapabilityEvaluationDto
        {
            CapabilityCode = capabilityCode,
            CapabilityName = capabilityName,
            Verdict = verdict,
            EvaluationAxis = axis,
            Strength = "rule_based",
            BasicEvaluation = message,
            ExtendedExplanation =
                "本项根据训练是否完整完成、训练模式以及设备助力、减重或阻力变化进行评价；提高难度但未完整完成时不判为改善。",
            LifeHint = _messageComposer.ComposeLifeHint(verdict, definition),
            PatientMessage = message,
            EvidenceMetricCodes = evidenceCodes,
            MessageRuleVersion = _messageComposer.Version,
            RuleSetVersion = Version
        };
    }

    private IReGoLongitudinalInterpretationDto Unavailable(
        IReGoMetricEvaluationInput input,
        IReGoCapabilityDefinition? capability)
    {
        var message = capability == null
            ? $"{input.MetricLabel}当前只展示记录值，暂不用于运动能力评价。"
            : $"{capability.Name}的可比记录不足，暂时无法评价。";
        var explanation = input.Values.Count < 3
            ? "同一项目、同一分段和同一训练条件下至少需要3条有效记录，当前数量不足。"
            : capability?.BetterDirection == "descriptive_only"
                ? "该指标当前仅用于展示，还没有经过审核的变化方向与评价阈值。"
                : "当前记录的单位、条件或评价阈值不满足自动评价要求。";
        var lifeHint = capability == null
            ? "该指标不能直接用于推断日常生活能力变化。"
            : _messageComposer.ComposeLifeHint(
                "insufficient_data",
                capability);
        return new IReGoLongitudinalInterpretationDto
        {
            MetricCode = input.MetricCode,
            Mode = "unavailable",
            Direction = "insufficient",
            Text = message,
            Verdict = "insufficient_data",
            CapabilityCode = capability?.Code ?? string.Empty,
            CapabilityName = capability?.Name ?? string.Empty,
            BasicEvaluation = message,
            ExtendedExplanation = explanation,
            LifeHint = lifeHint,
            PatientMessage = message,
            MessageRuleVersion = _messageComposer.Version,
            RuleSetVersion = Version
        };
    }

    private static decimal GameThreshold(decimal baseline) => Math.Max(
        Math.Abs(baseline) * GameRelativeChangeThreshold,
        GameAbsoluteChangeFloor);

    private static decimal Median(IEnumerable<decimal> source)
    {
        var values = source.OrderBy(x => x).ToArray();
        if (values.Length == 0)
        {
            return 0;
        }
        var middle = values.Length / 2;
        return values.Length % 2 == 1
            ? values[middle]
            : (values[middle - 1] + values[middle]) / 2m;
    }

    private static decimal TheilSenSlope(IReadOnlyList<decimal> values)
    {
        var slopes = new List<decimal>();
        for (var left = 0; left < values.Count - 1; left++)
        {
            for (var right = left + 1; right < values.Count; right++)
            {
                slopes.Add((values[right] - values[left]) / (right - left));
            }
        }
        return Median(slopes);
    }
}
