using AI_WebApi.Model.Domain.IReGo;
using AI_WebApi.Services.AITools.IReGo;

namespace AI_WebApi.Tests;

public sealed class IReGoEvaluationRuleServicesTests
{
    [Fact]
    public void Unrepaired_metric_uses_external_mdc_lower_bound()
    {
        var threshold = new IReGoThresholdRegistry().Resolve(
            "walking_average_speed_derived",
            "m/s",
            new Dictionary<string, decimal?>());

        Assert.NotNull(threshold);
        Assert.Equal(0.05m, threshold!.Value);
        Assert.Equal("external_mdc_lower_bound", threshold.Source);
        Assert.Equal(0.05m, threshold.ExternalLowerBound);
        Assert.False(threshold.IsIReGoMdc);
    }

    [Fact]
    public void Database_candidate_is_used_only_for_its_strict_condition()
    {
        var registry = new IReGoThresholdRegistry();
        var matched = registry.Resolve(
            "walking_step_width",
            "mm",
            new Dictionary<string, decimal?>
            {
                ["walking_mode"] = 0,
                ["unloading"] = 0,
                ["force"] = 0
            });
        var unmatched = registry.Resolve(
            "walking_step_width",
            "mm",
            new Dictionary<string, decimal?>
            {
                ["walking_mode"] = 2,
                ["unloading"] = 0,
                ["force"] = 0
            });

        Assert.Equal(31.49m, matched!.Value);
        Assert.Equal("database_exploratory", matched.Source);
        Assert.Equal(38.1m, unmatched!.Value);
        Assert.Equal("external_mdc_lower_bound", unmatched.Source);
    }

    [Theory]
    [InlineData("0.50,0.60,0.70", "improved", "您的平均步行速度连续提高，步行能力改善。")]
    [InlineData("0.50,0.52,0.53", "stable", "您的平均步行速度变化不大，步行能力无明显变化。")]
    [InlineData("0.70,0.60,0.50", "worsened", "您的平均步行速度连续下降，步行能力表现下降。")]
    [InlineData("0.50,0.70,0.40", "mixed", "您的步行能力表现有好有坏。")]
    public void Metric_verdict_uses_concise_patient_language(
        string values,
        string verdict,
        string expectedMessage)
    {
        var engine = Engine();
        var result = engine.EvaluateMetric(new IReGoMetricEvaluationInput
        {
            MetricCode = "walking_average_speed_derived",
            MetricLabel = "平均步行速度",
            Unit = "m/s",
            UnitStatus = "confirmed",
            Values = values.Split(',').Select(decimal.Parse).ToArray()
        });

        Assert.Equal(verdict, result.Verdict);
        Assert.Equal(expectedMessage, result.PatientMessage);
        Assert.Equal(result.PatientMessage, result.BasicEvaluation);
        Assert.NotEmpty(result.ExtendedExplanation);
        Assert.NotEmpty(result.LifeHint);
        Assert.DoesNotContain("步态时序控制", result.PatientMessage);
        Assert.DoesNotContain("病情", result.PatientMessage);
    }

    [Fact]
    public void Fewer_than_three_records_never_produces_a_verdict()
    {
        var result = Engine().EvaluateMetric(new IReGoMetricEvaluationInput
        {
            MetricCode = "walking_average_speed_derived",
            MetricLabel = "平均步行速度",
            Unit = "m/s",
            UnitStatus = "confirmed",
            Values = new[] { 0.5m, 0.8m }
        });

        Assert.Equal("insufficient_data", result.Verdict);
        Assert.Equal("unavailable", result.Mode);
    }

    [Fact]
    public void Walking_following_to_assistance_with_completion_improves_active_ability()
    {
        var result = Engine().EvaluateTrainingMode(
            new IReGoTrainingModeEvaluationInput
            {
                RehabContent = "walking",
                Observations = new[]
                {
                    Mode(1, true, 0, 20),
                    Mode(2, true, 1, 20)
                }
            });

        Assert.NotNull(result);
        Assert.Equal("improved", result!.Verdict);
        Assert.Equal("自主步行训练能力", result.CapabilityName);
        Assert.EndsWith("自主步行训练能力改善。", result.PatientMessage);
    }

    [Fact]
    public void Sit_passive_to_active_with_completion_improves_active_ability()
    {
        var result = Engine().EvaluateTrainingMode(
            new IReGoTrainingModeEvaluationInput
            {
                RehabContent = "sit_stand",
                Observations = new[]
                {
                    Mode(1, true, 1, 10),
                    Mode(2, true, 0, 10)
                }
            });

        Assert.Equal("improved", result!.Verdict);
        Assert.Equal(
            "您从被动训练过渡到主动训练并完整完成，自主坐站能力改善。",
            result.PatientMessage);
    }

    [Fact]
    public void Increased_support_is_mixed_not_automatically_worsened()
    {
        var result = Engine().EvaluateTrainingMode(
            new IReGoTrainingModeEvaluationInput
            {
                RehabContent = "walking",
                Observations = new[]
                {
                    Mode(1, true, 1, 10),
                    Mode(2, true, 1, 20)
                }
            });

        Assert.Equal("mixed", result!.Verdict);
        Assert.Equal(
            "您近期需要更多设备支持，自主步行训练能力表现有好有坏。",
            result.PatientMessage);
    }

    [Fact]
    public void Reduced_sit_stand_unloading_does_not_claim_mode_transition()
    {
        var result = Engine().EvaluateTrainingMode(
            new IReGoTrainingModeEvaluationInput
            {
                RehabContent = "sit_stand",
                Observations = new[]
                {
                    Mode(1, true, 0, 20),
                    Mode(2, true, 0, 10)
                }
            });

        Assert.Equal("improved", result!.Verdict);
        Assert.Equal(
            "您在更少减重支持下仍完整完成坐站训练，自主坐站能力改善。",
            result.PatientMessage);
        Assert.DoesNotContain("被动训练过渡到主动训练", result.PatientMessage);
    }

    [Fact]
    public void Latest_incomplete_training_never_produces_improvement()
    {
        var result = Engine().EvaluateTrainingMode(
            new IReGoTrainingModeEvaluationInput
            {
                RehabContent = "walking",
                Observations = new[]
                {
                    Mode(1, true, 0, 20),
                    Mode(2, false, 1, 10)
                }
            });

        Assert.Equal("insufficient_data", result!.Verdict);
        Assert.Contains("没有完整完成", result.PatientMessage);
    }

    [Theory]
    [InlineData("1.00,1.20,1.40", "improved", "持续提高")]
    [InlineData("1.00,1.05,1.08", "stable", "变化不大")]
    [InlineData("1.40,1.20,1.00", "worsened", "持续下降")]
    [InlineData("1.00,1.30,0.80", "mixed", "有升有降")]
    public void Same_difficulty_game_uses_score_rate_trend(
        string values,
        string verdict,
        string messagePart)
    {
        var result = Engine().EvaluateMetric(new IReGoMetricEvaluationInput
        {
            MetricCode = "game_score_rate",
            MetricLabel = "单位时间得分",
            Unit = "score/s",
            UnitStatus = "confirmed",
            Values = values.Split(',').Select(decimal.Parse).ToArray(),
            Conditions = new Dictionary<string, decimal?>
            {
                ["game_difficulty"] = 2
            }
        });

        Assert.Equal(verdict, result.Verdict);
        Assert.Contains(messagePart, result.BasicEvaluation);
        Assert.Contains("相同难度2", result.ExtendedExplanation);
        Assert.Contains("不是脑卒中临床量表阈值", result.ExtendedExplanation);
        Assert.Contains("持续注意、及时反应和手脚配合完成任务", result.LifeHint);
        Assert.DoesNotContain("不能单独说明", result.LifeHint);
        Assert.DoesNotContain("康复评估", result.LifeHint);
        Assert.Equal("rule_based_product_threshold", result.Strength);
    }

    [Fact]
    public void Harder_game_with_retained_score_rate_is_improved()
    {
        var result = Engine().EvaluateGamePerformance(new()
        {
            DisplayName = "平衡训练游戏",
            Observations = new[]
            {
                Game(1, true, 1, 1.00m),
                Game(2, true, 1, 1.10m),
                Game(3, true, 2, 1.00m)
            }
        });

        Assert.NotNull(result);
        Assert.Equal("improved", result!.Verdict);
        Assert.Contains("难度提高后", result.BasicEvaluation);
        Assert.Contains("90%", result.ExtendedExplanation);
        Assert.Equal("cross_difficulty_score_rate", result.EvaluationAxis);
    }

    [Fact]
    public void Harder_game_with_lower_efficiency_is_mixed()
    {
        var result = Engine().EvaluateGamePerformance(new()
        {
            DisplayName = "平衡训练游戏",
            Observations = new[]
            {
                Game(1, true, 1, 1.00m),
                Game(2, true, 1, 1.10m),
                Game(3, true, 2, 0.70m)
            }
        });

        Assert.Equal("mixed", result!.Verdict);
        Assert.Contains("有好有坏", result.BasicEvaluation);
    }

    [Fact]
    public void Easier_game_with_lower_efficiency_is_worsened()
    {
        var result = Engine().EvaluateGamePerformance(new()
        {
            DisplayName = "平衡训练游戏",
            Observations = new[]
            {
                Game(1, true, 3, 1.00m),
                Game(2, true, 3, 1.10m),
                Game(3, true, 2, 0.70m)
            }
        });

        Assert.Equal("worsened", result!.Verdict);
        Assert.Contains("难度降低后", result.BasicEvaluation);
        Assert.Contains("表现下降", result.BasicEvaluation);
    }

    [Fact]
    public void Incomplete_game_after_difficulty_change_is_mixed()
    {
        var result = Engine().EvaluateGamePerformance(new()
        {
            DisplayName = "平衡训练游戏",
            Observations = new[]
            {
                Game(1, true, 1, 1.00m),
                Game(2, true, 1, 1.10m),
                Game(3, false, 2, null)
            }
        });

        Assert.Equal("mixed", result!.Verdict);
        Assert.Contains("没有完整完成", result.BasicEvaluation);
        Assert.DoesNotContain("表现改善", result.BasicEvaluation);
    }

    private static IReGoTrainingModeObservation Mode(
        int ordinal,
        bool complete,
        int mode,
        decimal unloading) => new()
        {
            ReportOrdinal = ordinal,
            IsComplete = complete,
            ModeCode = mode,
            Unloading = unloading
        };

    private static IReGoGamePerformanceObservation Game(
        int ordinal,
        bool complete,
        int difficulty,
        decimal? scoreRate) => new()
        {
            ReportOrdinal = ordinal,
            IsComplete = complete,
            Difficulty = difficulty,
            ScoreRate = scoreRate
        };

    private static IReGoEvaluationRuleEngine Engine() => new(
        new IReGoThresholdRegistry(),
        new IReGoStrokeCapabilityMapper(),
        new IReGoPatientMessageComposer());
}
