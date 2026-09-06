using AI_WebApi.Model.AITools;
using AI_WebApi.Model.DTOs.AITools;
using AI_WebApi.Services.AITools.IReGo;

namespace AI_WebApi.Tests;

public sealed class IReGoReportParserTests
{
    private readonly IReGoReportParser _parser = new();

    [Fact]
    public void Parses_primary_report_list_into_separate_content_blocks()
    {
        const string json = @"{
          ""HealthScore"": 99,
          ""GameScore"": 88,
          ""ReportList"": [
            { ""ReportMode"": 1, ""WTrainingWalktime"": 2, ""WTrainingWalkdistance"": 15 },
            { ""ReportMode"": 2, ""STrainingSitTime"": 1, ""STrainingSitNum"": 7 },
            { ""ReportMode"": 3, ""GameMode"": ""demo"", ""GTrainingTime"": 0 },
            { ""ReportMode"": 4, ""BalanceMode"": 5, ""RTrainingBalanceTime"": 0 }
          ],
          ""ReportProcess"": ""1234""
        }";

        var result = _parser.Parse(json);

        Assert.True(result.ParseSucceeded);
        Assert.True(result.HasReportList);
        Assert.Equal(4, result.ReportItemCount);
        Assert.Equal(2, result.DirectTraining.Count);
        Assert.Single(result.Games);
        Assert.Single(result.Assessments);
        Assert.Equal("balance_assessment", result.Assessments[0].RehabContent);
        Assert.DoesNotContain(
            result.DirectTraining.SelectMany(x => x.Metrics),
            x => x.SourceField is "HealthScore" or "GameScore");
    }

    [Fact]
    public void Preserves_zero_and_excludes_only_non_finite_metric()
    {
        const string json = @"{
          ""ReportList"": [
            {
              ""ReportMode"": 4,
              ""BalanceMode"": 4,
              ""RTrainingBalanceTime"": 0,
              ""cLBalanceVision"": {
                ""RombergArea"": ""NaN"",
                ""RombergLength"": 12.5
              }
            }
          ],
          ""ReportProcess"": ""4""
        }";

        var result = _parser.Parse(json);
        var metrics = result.DirectTraining.Single().Metrics;

        Assert.Contains(
            metrics,
            x => x.MetricCode == "balance_training_duration" &&
                 x.NormalizedValue == 0 &&
                 x.ValueStatus == "valid");
        Assert.Contains(
            metrics,
            x => x.MetricCode == "romberg_area" &&
                 x.NormalizedValue == null &&
                 x.ValueStatus == "non_finite");
        Assert.Contains(
            metrics,
            x => x.MetricCode == "romberg_length" &&
                 x.NormalizedValue == 12.5m);
        Assert.Contains(
            result.Issues,
            x => x.Code == IReGoReasonCodes.NonFiniteMetric);
    }

    [Fact]
    public void Uses_report_process_only_for_consistency_warning()
    {
        const string json = @"{
          ""ReportList"": [
            { ""ReportMode"": 1, ""WDistance"": 0 },
            { ""ReportMode"": 2, ""SNumber"": 0 }
          ],
          ""ReportProcess"": ""11""
        }";

        var result = _parser.Parse(json);

        Assert.Equal(2, result.DirectTraining.Count);
        Assert.Contains(
            result.Issues,
            x => x.Code == IReGoReasonCodes.ReportProcessMismatch);
    }

    [Fact]
    public void Schema_signature_is_independent_of_property_order()
    {
        const string first =
            @"{""ReportList"":[{""ReportMode"":1,""WDistance"":2}],""ReportProcess"":""1""}";
        const string second =
            @"{""ReportProcess"":""1"",""ReportList"":[{""WDistance"":2,""ReportMode"":1}]}";

        Assert.Equal(
            _parser.Parse(first).SchemaSignature,
            _parser.Parse(second).SchemaSignature);
    }

    [Fact]
    public void Walking_blocks_keep_report_order_and_receive_readable_names()
    {
        const string json = """
            {
              "ReportList": [
                { "ReportMode": 1, "WTrainingWalktime": 41 },
                { "ReportMode": 1, "WTrainingWalktime": 180 },
                { "ReportMode": 1, "WTrainingWalktime": 42 }
              ],
              "ReportProcess": "111"
            }
            """;

        var result = _parser.Parse(json);

        Assert.Equal(3, result.ReportItemCount);
        Assert.Equal(3, result.ReportBlockCount);
        Assert.Equal(new[] { 1, 2, 3 }, result.DirectTraining.Select(x => x.Sequence));
        Assert.Equal(
            new[] { "步行训练第1段", "步行训练第2段", "步行训练第3段" },
            result.DirectTraining.Select(x => x.DisplayName));
        Assert.All(
            result.DirectTraining,
            block => Assert.Equal(block.DisplayName, block.Name));
        Assert.Equal(
            new[] { 41m, 180m, 42m },
            result.DirectTraining.Select(block =>
                block.Metrics.Single(x =>
                    x.SourceField == "WTrainingWalktime").NormalizedValue!.Value)
                .ToArray());
        Assert.All(
            result.DirectTraining.SelectMany(x => x.Metrics).Where(x =>
                x.SourceField == "WTrainingWalktime"),
            metric =>
            {
                Assert.Equal("s", metric.Unit);
                Assert.Equal("confirmed", metric.UnitStatus);
            });
    }

    [Fact]
    public void Game_uses_source_name_and_unknown_uses_generated_block_name()
    {
        const string json = """
            {
              "ReportList": [
                { "ReportMode": 3, "GameName": "宇宙拼图", "GameMode": "备用名称" },
                { "ReportMode": 3 },
                { "ReportMode": 99 }
              ],
              "ReportProcess": "3399"
            }
            """;

        var result = _parser.Parse(json);

        Assert.Equal("宇宙拼图", result.Games[0].DisplayName);
        Assert.Equal("训练游戏第2段", result.Games[1].DisplayName);
        Assert.Equal("训练记录第1段", result.DirectTraining[0].DisplayName);
    }

    [Fact]
    public void Robotdb_modes_five_and_six_have_distinct_content_types()
    {
        const string json = """
            {
              "ReportList": [
                { "ReportMode": 5, "WTrainingWalktime": 2 },
                { "ReportMode": 6, "STrainingSitTime": 3 }
              ],
              "ReportProcess": "56"
            }
            """;

        var result = _parser.Parse(json);

        Assert.Equal("robotdb_reportlist_v1", result.SchemaFamily);
        Assert.Equal("single_walking", result.DirectTraining[0].RehabContent);
        Assert.Equal("单侧步行训练第1段", result.DirectTraining[0].DisplayName);
        Assert.Equal("single_sit", result.DirectTraining[1].RehabContent);
        Assert.Equal("单侧坐站训练第1段", result.DirectTraining[1].DisplayName);
    }

    [Fact]
    public void Confirmed_game_mode_name_and_score_are_returned()
    {
        const string json = """
            {
              "ReportList": [
                { "ReportMode": 3, "GameMode": 3, "GScore": 10 }
              ],
              "ReportProcess": "3"
            }
            """;

        var game = Assert.Single(_parser.Parse(json).Games);

        Assert.Equal("宇宙拼图", game.DisplayName);
        var score = Assert.Single(game.Metrics.Where(x =>
            x.MetricCode == "game_score"));
        Assert.Equal(10m, score.NormalizedValue);
        Assert.Equal("GScore", score.SourceField);
    }

    [Fact]
    public void Game_score_per_second_is_derived_but_remains_descriptive()
    {
        const string json = """
            {
              "ReportList": [
                {
                  "ReportMode": 3,
                  "GameMode": 3,
                  "GScore": 120,
                  "GTrainingTime": 60
                }
              ],
              "ReportProcess": "3"
            }
            """;

        var game = Assert.Single(_parser.Parse(json).Games);
        var rate = Assert.Single(game.Metrics.Where(x =>
            x.MetricCode == "game_score_rate"));

        Assert.Equal(2m, rate.NormalizedValue);
        Assert.Equal("score/s", rate.Unit);
        Assert.Equal("confirmed", rate.UnitStatus);
        Assert.Equal("derived", rate.SourceKind);
        Assert.Equal(
            "descriptive_only_scoring_version_missing",
            rate.EvaluationStatus);
    }

    [Fact]
    public void Unknown_numeric_game_mode_is_preserved_without_inventing_a_name()
    {
        const string json = """
            {
              "ReportList": [
                { "ReportMode": 3, "GameMode": 999, "GScore": 6 }
              ],
              "ReportProcess": "3"
            }
            """;

        var game = Assert.Single(_parser.Parse(json).Games);

        Assert.Equal("GameMode 999", game.DisplayName);
    }

    [Fact]
    public void Confirmed_duration_units_are_structured_as_seconds()
    {
        const string json = """
            {
              "ReportList": [
                { "ReportMode": 2, "STrainingSitTime": 5 },
                { "ReportMode": 3, "GTrainingTime": 6 },
                { "ReportMode": 4, "RTrainingBalanceTime": 7 }
              ],
              "ReportProcess": "234"
            }
            """;

        var result = _parser.Parse(json);
        var durationMetrics = result.DirectTraining
            .Concat(result.Games)
            .Concat(result.Assessments)
            .SelectMany(x => x.Metrics)
            .Where(x => x.MetricCode.Contains("duration", StringComparison.Ordinal))
            .ToArray();

        Assert.Equal(3, durationMetrics.Length);
        var sitStand = Assert.Single(durationMetrics.Where(x =>
            x.MetricCode == "sit_stand_training_duration"));
        Assert.Equal("s", sitStand.Unit);
        Assert.Equal("confirmed", sitStand.UnitStatus);
        Assert.All(durationMetrics, metric =>
        {
            Assert.Equal("s", metric.Unit);
            Assert.Equal("confirmed", metric.UnitStatus);
        });
    }

    [Fact]
    public void Confirmed_metric_units_use_existing_metric_shape()
    {
        const string json = """
            {
              "ReportList": [
                {
                  "ReportMode": 1,
                  "WAvgSpeed": 0.8,
                  "WMaxSpeed": 1.1,
                  "WStepFreAgv": 72,
                  "WStepLenAgv": 410,
                  "WStepTAgv": 0.7,
                  "WStepWidAgv": 85,
                  "WImponderability": 15,
                  "WTime": 5,
                  "WAvgGaitPeriod": 1.2,
                  "WAvgStep": 0.82,
                  "WalkReportList": [
                    {
                      "Time": "01:49",
                      "TotalTime": 5,
                      "Assistance": 2,
                      "Resistance": 1,
                      "Weight": 15
                    }
                  ]
                },
                {
                  "ReportMode": 2,
                  "StableSSTimeUp": 1.1,
                  "StableSSTimeDown": 1.2,
                  "SAvgTime": 3,
                  "SMinTime": 2,
                  "SMaxTime": 4,
                  "STime": 6,
                  "SitReportList": [
                    {
                      "SitTime": 2,
                      "StandTime": 3,
                      "Speed": 20,
                      "WeightHoss": 10
                    }
                  ]
                },
                { "ReportMode": 3, "GTrainingTime": 30, "GTime": 35 },
                { "ReportMode": 4, "RTrainingBalanceTime": 40, "BTime": 45 }
              ],
              "ReportProcess": "1234"
            }
            """;

        var result = _parser.Parse(json);
        var metrics = result.DirectTraining
            .Concat(result.Games)
            .Concat(result.Assessments)
            .SelectMany(x => x.Metrics)
            .ToDictionary(x => x.MetricCode, StringComparer.Ordinal);

        AssertMetric(metrics, "walking_average_speed", "m/s");
        AssertMetric(metrics, "walking_max_speed", "m/s");
        AssertMetric(metrics, "walking_step_frequency", "step/min");
        AssertMetric(metrics, "walking_step_length", "m");
        AssertMetric(metrics, "walking_step_time", "s");
        AssertMetric(metrics, "walking_step_width", "mm");
        AssertMetric(metrics, "walking_unloading", "kg");
        AssertMetric(metrics, "walking_set_time", "min");
        AssertMetric(metrics, "walking_display_assistance", "N");
        AssertMetric(metrics, "walking_display_resistance", "N");
        AssertMetric(metrics, "walking_display_weight", "kg");
        AssertMetric(metrics, "sit_stand_stability_up_time", "s");
        AssertMetric(metrics, "sit_stand_stability_down_time", "s");
        AssertMetric(metrics, "sit_stand_average_time", "s");
        AssertMetric(metrics, "sit_stand_min_time", "s");
        AssertMetric(metrics, "sit_stand_max_time", "s");
        AssertMetric(metrics, "sit_stand_set_time", "min");
        AssertMetric(metrics, "sit_display_hold_time", "s");
        AssertMetric(metrics, "stand_display_hold_time", "s");
        AssertMetric(metrics, "sit_stand_display_speed", "mm/s");
        AssertMetric(metrics, "sit_stand_display_unloading", "kg");
        AssertMetric(metrics, "game_training_duration", "s");
        AssertMetric(metrics, "game_set_time", "s");
        AssertMetric(metrics, "balance_training_duration", "s");
        AssertMetric(metrics, "balance_set_or_record_time", "s");

        var displayTime = metrics["walking_display_time"];
        Assert.Equal("01:49", displayTime.RawValue);
        Assert.Null(displayTime.NormalizedValue);
        Assert.Equal("valid", displayTime.ValueStatus);
        Assert.Equal("mm:ss", displayTime.Unit);
        Assert.Equal("confirmed", displayTime.UnitStatus);

        Assert.Equal(
            "confirmed",
            metrics["walking_average_gait_period"].UnitStatus);
        Assert.Equal(
            "confirmed",
            metrics["walking_average_stride"].UnitStatus);
    }

    [Fact]
    public void Positive_distance_with_zero_steps_and_speed_is_flagged()
    {
        const string json = """
            {
              "ReportList": [
                {
                  "ReportMode": 1,
                  "WTrainingWalkdistance": 10,
                  "WSteps": 0,
                  "WAvgSpeed": 0
                }
              ],
              "ReportProcess": "1"
            }
            """;

        var result = _parser.Parse(json);
        var issue = Assert.Single(result.Issues.Where(x =>
            x.Code == IReGoReasonCodes.WalkingDistanceWithoutStepsOrSpeed));

        Assert.False(string.IsNullOrWhiteSpace(issue.PatientMessage));
    }

    private static void AssertMetric(
        IReadOnlyDictionary<string, MetricFactDto> metrics,
        string code,
        string unit)
    {
        Assert.Equal(unit, metrics[code].Unit);
        Assert.Equal("confirmed", metrics[code].UnitStatus);
    }

    private static void AssertUnverified(
        IReadOnlyDictionary<string, MetricFactDto> metrics,
        string code)
    {
        Assert.Null(metrics[code].Unit);
        Assert.Equal("unverified", metrics[code].UnitStatus);
    }

    [Fact]
    public void Zero_raw_walking_speed_is_preserved_and_derived_speed_is_added()
    {
        var result = _parser.Parse("""
            {"ReportList":[{
              "ReportMode":1,
              "WTrainingWalktime":100,
              "WTrainingWalkdistance":50,
              "WAvgSpeed":0,
              "WMaxSpeed":0,
              "WSteps":0
            }]}
            """);

        var metrics = result.DirectTraining.Single().Metrics;
        var raw = metrics.Single(x => x.MetricCode == "walking_average_speed");
        var derived = metrics.Single(x =>
            x.MetricCode == "walking_average_speed_derived");

        Assert.Equal(0m, raw.NormalizedValue);
        Assert.Equal("default_zero_unusable", raw.ValueStatus);
        Assert.Equal(0.5m, derived.NormalizedValue);
        Assert.Equal("derived", derived.SourceKind);
        Assert.Equal("distance_m_div_duration_s", derived.Repair!.Rule);
    }

    [Fact]
    public void Nested_balance_metrics_and_romberg_formula_are_structured()
    {
        var result = _parser.Parse("""
            {"ReportList":[{
              "ReportMode":4,
              "BalanceMode":5,
              "cLBalanceVision":{
                "OpenLength":100,
                "CloseLength":200,
                "OpenArea":50,
                "CloseArea":100
              }
            }]}
            """);

        var metrics = result.Assessments.Single().Metrics;
        Assert.Equal("mm", metrics.Single(x =>
            x.MetricCode == "vision_open_length").Unit);
        Assert.Equal(50m, metrics.Single(x =>
            x.MetricCode == "romberg_length_recomputed").NormalizedValue);
        Assert.Equal(50m, metrics.Single(x =>
            x.MetricCode == "romberg_area_recomputed").NormalizedValue);
    }
}
