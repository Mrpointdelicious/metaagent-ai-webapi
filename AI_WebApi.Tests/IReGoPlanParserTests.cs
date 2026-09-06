using AI_WebApi.Services.AITools.IReGo;

namespace AI_WebApi.Tests;

public sealed class IReGoPlanParserTests
{
    [Fact]
    public void Parses_plan_blocks_without_changing_metric_fact_shape()
    {
        const string json = """
            [
              {
                "ReportMode": 1,
                "speed": 0.8,
                "assistance": 2,
                "resistance": 1,
                "weightLoss": 15
              },
              {
                "ReportMode": 2,
                "sitTime": 3,
                "standTime": 4
              }
            ]
            """;

        var result = IReGoPlanParser.Parse(json, null);

        Assert.True(result.ParseSucceeded);
        Assert.Equal("irego_plan_array", result.SchemaFamily);
        Assert.Equal(2, result.Blocks.Count);
        Assert.Equal("walking", result.Blocks[0].RehabContent);
        Assert.Equal("sit_stand", result.Blocks[1].RehabContent);

        var walking = result.Blocks[0].Metrics.ToDictionary(x => x.MetricCode);
        Assert.Equal("m/s", walking["plan_walking_speed"].Unit);
        Assert.Equal("confirmed", walking["plan_walking_speed"].UnitStatus);
        Assert.Equal("N", walking["plan_assistance"].Unit);
        Assert.Equal("N", walking["plan_resistance"].Unit);
        Assert.Equal("kg", walking["plan_weight_loss"].Unit);
        Assert.All(walking.Values, x => Assert.Equal("plan_derived", x.DataStage));

        var sitStand = result.Blocks[1].Metrics.ToDictionary(x => x.MetricCode);
        Assert.Equal("s", sitStand["plan_sit_hold_duration"].Unit);
        Assert.Equal("s", sitStand["plan_stand_hold_duration"].Unit);
    }

    [Fact]
    public void Unknown_plan_speed_keeps_unit_unverified()
    {
        const string json = """{ "speed": 20 }""";

        var result = IReGoPlanParser.Parse(json, null);
        var speed = Assert.Single(result.Blocks).Metrics.Single();

        Assert.Equal("plan_speed", speed.MetricCode);
        Assert.Null(speed.Unit);
        Assert.Equal("unverified", speed.UnitStatus);
    }

    [Fact]
    public void Robotdb_template_game_mode_uses_registry_or_preserves_number()
    {
        const string json = """
            {
              "templateData": [
                { "type": "Gaming", "Gaming_Mode": 3 },
                { "type": "Gaming", "Gaming_Mode": 999 }
              ]
            }
            """;

        var result = IReGoPlanParser.Parse(json, null);

        Assert.Equal("robotdb_plan_template_v1", result.SchemaFamily);
        Assert.Equal("宇宙拼图", result.Blocks[0].DisplayName);
        Assert.Equal("GameMode 999", result.Blocks[1].DisplayName);
    }
}
