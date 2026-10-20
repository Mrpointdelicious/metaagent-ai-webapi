using AI_WebApi.Model.DTOs.AITools;
using AI_WebApi.Services.AITools.IReTour;

namespace AI_WebApi.Tests;

public sealed class IReTourReportHtmlBuilderTests
{
    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(0, 0.5, 1)]
    [InlineData(1, 1, 1)]
    public void Longitudinal_rates_use_percentage_scale_for_ticks_and_values(
        decimal first, decimal second, decimal third)
    {
        var points = new[] { Point(1, first), Point(2, second), Point(3, third) };
        var analysis = new IReTourLongitudinalAnalysisDto
        {
            Sessions = points.Select(point => new IReTourLongitudinalSessionDto
            {
                Ordinal = point.Ordinal,
                SessionTime = point.SessionTime,
                CanonicalCode = "reaction_primary"
            }).ToArray(),
            MetricSeries = new[]
            {
                new IReTourMetricSeriesDto
                {
                    MetricCode = "reaction_correct_rate", DisplayName = "正确率", Points = points
                }
            }
        };

        var html = new IReTourLongitudinalReportHtmlBuilder().Build(analysis);
        var start = html.IndexOf("<svg", StringComparison.Ordinal);
        var end = html.IndexOf("</svg>", start, StringComparison.Ordinal) + "</svg>".Length;
        var svg = System.Xml.Linq.XElement.Parse(html[start..end]);
        var ticks = svg.Elements("text").Where(x => (string?)x.Attribute("class") == "axis");
        Assert.Equal(4, ticks.Count());
        var tickValues = ticks.Select(tick => decimal.Parse(
            tick.Value, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        Assert.True(tickValues.Min() < new[] { first, second, third }.Min() * 100m);
        Assert.True(tickValues.Max() > new[] { first, second, third }.Max() * 100m);
        Assert.Contains("单位：%", html);
        Assert.Equal(3, svg.Elements("circle").Count());
        Assert.All(svg.Elements("text").Where(x => (string?)x.Attribute("class") == "value-label"),
            label => Assert.EndsWith("%", label.Value));
    }

    [Fact]
    public void Single_report_uses_irego_layout_and_patient_facing_fields()
    {
        var analysis = new IReTourSessionAnalysisDto
        {
            Session = new IReTourSessionIdentityDto
            {
                SessionRef = "iretour_s_v1_internal",
                PatientRef = "iretour_patient_internal"
            },
            Time = new IReTourSessionTimeDto
            {
                ExecutionStartedAt = DateTimeOffset.Parse("2026-01-02T09:00:00+08:00"),
                ExecutionEndedAt = DateTimeOffset.Parse("2026-01-02T09:02:00+08:00"),
                TrainingState = "completed"
            },
            Activity = new IReTourActivityBriefDto
            {
                CanonicalCode = "reaction_primary",
                DisplayName = "反应训练 <一级>"
            },
            Plan = new IReTourSessionPlanDto
            {
                DurationSeconds = 120,
                Parameters = new[]
                {
                    new IReTourPlanParameterDto
                    {
                        Code = "difficulty",
                        DisplayName = "训练难度",
                        Value = "2"
                    }
                }
            },
            Result = new IReTourSessionResultDto
            {
                ResultState = "summary_available",
                Metrics = new[]
                {
                    new IReTourMetricDto
                    {
                        MetricCode = "reaction_correct_rate",
                        DisplayName = "正确率",
                        NormalizedValue = 0.925m,
                        ValueStatus = "valid"
                    },
                    new IReTourMetricDto
                    {
                        MetricCode = "reaction_speed",
                        DisplayName = "平均反应速度",
                        NormalizedValue = 1.2m,
                        Unit = "秒",
                        ValueStatus = "valid"
                    }
                },
                RawEvidence = new IReTourRawEvidenceDto { Available = true }
            },
            Sources = new[]
            {
                new IReTourSourceFactDto { SourceTable = "internal_walk_table" }
            }
        };

        var html = new IReTourSingleSessionReportHtmlBuilder().Build(analysis);

        Assert.Contains(
            "background:linear-gradient(110deg,#1d536f 0%,#2786a5 53%,#38a79e 100%)",
            html);
        Assert.Contains("01 &#183; 训练总览", html);
        Assert.Contains("02 &#183; 设置与结果", html);
        Assert.Contains("03 &#183; 训练详情", html);
        Assert.Contains("记录设置与训练结果", html);
        Assert.Contains("92.5%", html);
        Assert.Contains("1.2 秒", html);
        Assert.Contains("反应训练 &lt;一级&gt;", html);
        Assert.DoesNotContain("iretour_s_v1_internal", html);
        Assert.DoesNotContain("iretour_patient_internal", html);
        Assert.DoesNotContain("internal_walk_table", html);
    }

    [Fact]
    public void Longitudinal_report_preserves_missing_positions_in_irego_chart_style()
    {
        var sessions = Enumerable.Range(1, 4)
            .Select(index => new IReTourLongitudinalSessionDto
            {
                Ordinal = index,
                SessionRef = $"internal-{index}",
                SessionTime = DateTimeOffset.Parse(
                    $"2026-01-0{index}T09:00:00+08:00"),
                CanonicalCode = "straight_primary",
                TrainingState = "completed",
                ResultState = "summary_available"
            })
            .ToArray();
        var analysis = new IReTourLongitudinalAnalysisDto
        {
            PatientRef = "iretour_patient_internal",
            Window = new IReTourLongitudinalWindowDto
            {
                ResolvedCount = 4,
                ComparableCount = 3,
                StartedAt = sessions[0].SessionTime,
                EndedAt = sessions[^1].SessionTime
            },
            Sessions = sessions,
            MetricSeries = new[]
            {
                new IReTourMetricSeriesDto
                {
                    MetricCode = "straight_speed",
                    DisplayName = "平均速度 <趋势>",
                    Unit = "m/s",
                    Direction = "increased",
                    Points = new[]
                    {
                        Point(1, 0.4m),
                        new IReTourMetricPointDto
                        {
                            Ordinal = 2,
                            SessionTime = sessions[1].SessionTime,
                            ValueStatus = "missing"
                        },
                        Point(3, 0.6m),
                        Point(4, 0.7m)
                    }
                }
            }
        };

        var html = new IReTourLongitudinalReportHtmlBuilder().Build(analysis);

        Assert.Contains("IRETOUR · INTEGRATED REPORT", html);
        Assert.Contains("01 &#183; OVERVIEW", html);
        Assert.Contains("02 &#183; SAME PROJECT", html);
        Assert.Contains("viewBox=\"0 0 960 232\"", html);
        Assert.Contains(">2026-01-02</text>", html);
        Assert.Contains(">2026-01-04</text>", html);
        Assert.Equal(3, System.Text.RegularExpressions.Regex.Matches(html, "<circle class=\"point\"").Count);
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(html, "<line class=\"series\"").Count);
        Assert.Contains("平均速度 &lt;趋势&gt;", html);
        Assert.Contains("同一指标有三个及以上有效记录方可展示", html);
        Assert.Contains("不能据此判断康复效果", html);
        Assert.DoesNotContain("iretour_patient_internal", html);
        Assert.DoesNotContain("internal-1", html);
    }

    [Fact]
    public void Longitudinal_report_hides_series_with_fewer_than_three_valid_points()
    {
        var analysis = new IReTourLongitudinalAnalysisDto
        {
            Window = new IReTourLongitudinalWindowDto { ResolvedCount = 3 },
            Sessions = Enumerable.Range(1, 3)
                .Select(index => new IReTourLongitudinalSessionDto
                {
                    Ordinal = index,
                    CanonicalCode = "straight_primary"
                })
                .ToArray(),
            MetricSeries = new[]
            {
                new IReTourMetricSeriesDto
                {
                    MetricCode = "straight_speed",
                    DisplayName = "不应展示的指标",
                    Points = new[] { Point(1, 0.4m), Point(2, 0.5m) }
                }
            }
        };

        var html = new IReTourLongitudinalReportHtmlBuilder().Build(analysis);

        Assert.Contains("训练记录还不够，暂时无法展示数值变化", html);
        Assert.DoesNotContain("不应展示的指标", html);
        Assert.DoesNotContain("SAME PROJECT", html);
    }

    private static IReTourMetricPointDto Point(int ordinal, decimal value) =>
        new()
        {
            Ordinal = ordinal,
            SessionTime = DateTimeOffset.Parse(
                $"2026-01-0{ordinal}T09:00:00+08:00"),
            Value = value,
            ValueStatus = "valid"
        };
}
