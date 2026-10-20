using System.Text.RegularExpressions;
using AI_WebApi.Model.DTOs.AITools;
using AI_WebApi.Services.AITools.IReGo;
using AI_WebApi.Services.AITools.IReTour;

namespace AI_WebApi.Tests;

public sealed class IReTourReportTemplateParityTests
{
    [Fact]
    public void Single_report_has_identical_irego_styles_and_duration_panel()
    {
        var block = new IReGoContentBlockDto
        {
            Sequence = 1, RehabContent = "walking", DisplayName = "直行训练", Name = "直行训练",
            Metrics = new[]
            {
                new MetricFactDto
                {
                    MetricCode = "walking_training_duration", RawValue = 120m, NormalizedValue = 120m,
                    Unit = "s", UnitStatus = "confirmed", ValueStatus = "valid"
                }
            }
        };
        var go = new IReGoSingleSessionReportHtmlBuilder().Build(new()
        {
            Plan = new IReGoPlanDetailDto { Blocks = new[] { block } },
            ReportBlocks = new[] { block },
            BlockComparison = new[]
            {
                new IReGoBlockComparisonDto { PlanSequence = 1, ReportSequence = 1, Status = "matched_exact" }
            }
        }, new IReGoSingleSessionAiEvaluationDto());
        var tour = new IReTourSingleSessionReportHtmlBuilder().Build(new()
        {
            Activity = new IReTourActivityBriefDto { CanonicalCode = "straight_primary", DisplayName = "步行训练" },
            Plan = new IReTourSessionPlanDto { DurationSeconds = 120 }
        });

        Assert.Equal(Styles(go), Styles(tour));
        Assert.Equal(DurationPanel(go), DurationPanel(tour));
        Assert.Contains(".hero-meta{grid-template-columns:repeat(4,1fr)}", Styles(tour));
        foreach (var section in new[] { "overview", "compare-header", "compare-row", "segment-grid", "field-list", "disclaimer" })
        {
            Assert.Contains($"class=\"{section}\"", go);
            Assert.Contains($"class=\"{section}\"", tour);
        }
    }

    [Theory]
    [InlineData(4, false)]
    [InlineData(20, false)]
    [InlineData(4, true)]
    public void Longitudinal_report_has_identical_irego_styles_and_chart_markup(int count, bool rate)
    {
        var positions = Enumerable.Range(1, count).Select(index => new IReGoLongitudinalReportPositionDto
        {
            Ordinal = index,
            SessionTime = new DateTimeOffset(2026, 1, index, 9, 0, 0, TimeSpan.FromHours(8))
        }).ToArray();
        var points = positions.Select(position => new IReTourMetricPointDto
        {
            Ordinal = position.Ordinal, SessionTime = position.SessionTime,
            ValueStatus = position.Ordinal == 2 ? "missing" : "valid",
            Value = position.Ordinal == 2 ? null : position.Ordinal / 20m
        }).ToArray();
        var code = rate ? "reaction_correct_rate" : "straight_speed";
        var label = rate ? "正确率" : "平均速度";
        var unit = rate ? "%" : "m/s";
        var goSeries = new IReGoLongitudinalMetricSeriesDto
        {
            MetricCode = code, DisplayName = label, Unit = unit,
            Points = points.Select(point => new IReGoLongitudinalMetricPointDto
            {
                ReportOrdinal = point.Ordinal, Status = point.ValueStatus,
                NormalizedValue = rate ? point.Value * 100 : point.Value,
                DisplayValue = point.Value.HasValue
                    ? (rate ? point.Value.Value * 100 : point.Value.Value).ToString(
                        "0.###", System.Globalization.CultureInfo.InvariantCulture) + (rate ? "%" : "")
                    : null
            }).ToArray()
        };
        var go = new IReGoLongitudinalReportHtmlBuilder().Build(new()
        {
            Reports = positions,
            Projects = new[]
            {
                new IReGoLongitudinalProjectDto
                {
                    DisplayName = "训练", RehabContent = "walking",
                    Lanes = new[]
                    {
                        new IReGoLongitudinalLaneDto { MetricSeries = new[] { goSeries } }
                    }
                }
            }
        });
        var tour = new IReTourLongitudinalReportHtmlBuilder().Build(new()
        {
            Sessions = positions.Select(position => new IReTourLongitudinalSessionDto
            {
                Ordinal = position.Ordinal, SessionTime = position.SessionTime, CanonicalCode = "straight_primary"
            }).ToArray(),
            MetricSeries = new[]
            {
                new IReTourMetricSeriesDto { MetricCode = code, DisplayName = label, Unit = rate ? null : unit, Points = points }
            }
        });

        Assert.Equal(Styles(go), Styles(tour));
        var goChart = Regex.Match(go, "<section class=\"metric-card\">.*?</section>", RegexOptions.Singleline).Value;
        var tourChart = Regex.Match(tour, "<section class=\"metric-card\">.*?</section>", RegexOptions.Singleline).Value;
        Assert.NotEmpty(goChart);
        Assert.Equal(goChart, tourChart);
        Assert.Equal(count - 1, Regex.Matches(tourChart, "<circle class=\"point\"").Count);
        Assert.InRange(Regex.Matches(tourChart, "class=\"axis report-label\"").Count, 1, 10);
    }

    private static string Styles(string html) =>
        Regex.Match(html, "<style>(.*?)</style>", RegexOptions.Singleline).Groups[1].Value;

    private static string DurationPanel(string html)
    {
        var start = html.IndexOf("<div class=\"duration-wrap\">", StringComparison.Ordinal);
        Assert.True(start >= 0);
        return html[start..html.IndexOf("</section>", start, StringComparison.Ordinal)];
    }
}
