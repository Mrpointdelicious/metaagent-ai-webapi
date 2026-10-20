using AI_WebApi.Services.AITools.IReTour;

namespace AI_WebApi.Tests;

public sealed class IReTourProjectSchemaTests
{
    [Theory]
    [InlineData(0, "g_averageWalkingSpeed", "straight_speed", 0.6)]
    [InlineData(2, "g_correctRate", "reaction_correct_rate", 0.8)]
    [InlineData(4, "g_leftDeviationRate", "balance_left_deviation_rate", 0.2)]
    public void ProjectSummaryAliasesProduceFactsWithoutInventingUnits(
        int item, string field, string metric, double value)
    {
        var parser = IReTourSummaryParserRegistry.Resolve(item)!;
        var json = Newtonsoft.Json.JsonConvert.SerializeObject(
            new Dictionary<string, object> { [field] = value, ["name"] = "synthetic" });
        var result = IReTourSummaryExtraction.Parse(json, parser);
        var fact = Assert.Single(result.Metrics);
        Assert.Equal(metric, fact.MetricCode);
        Assert.Equal((decimal)value, fact.NormalizedValue);
        Assert.Null(fact.Unit);
        Assert.DoesNotContain(result.Metrics, x => x.SourceField == "name");
    }

    [Fact]
    public void ConflictingAliasesAreExcluded()
    {
        var result = IReTourSummaryExtraction.Parse(
            "{\"avg_Speed\":0.5,\"g_averageWalkingSpeed\":0.8}", new StraightSummaryParser());
        Assert.Null(Assert.Single(result.Metrics).NormalizedValue);
        Assert.Contains(result.Issues, x => x.Code == "IRETOUR_METRIC_ALIAS_CONFLICT");
    }
}
