using AI_WebApi.Model.AITools;
using AI_WebApi.Model.Entities;
using AI_WebApi.Services.AITools.IReGo;

namespace AI_WebApi.Tests;

public sealed class TrainingStateResolverTests
{
    private static readonly TimeZoneInfo China =
        IReGoPatientEvaluationService.ChinaTimeZone();

    [Fact]
    public void Completed_does_not_require_an_end_time_in_robotdb_main()
    {
        var result = TrainingStateResolver.Resolve(
            new RehabPlanEntity { IsComplete = 1 },
            AtChina(2026, 7, 27, 10),
            China);

        Assert.Equal("completed", result.State);
        Assert.Equal("high", result.Confidence);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void Incomplete_before_booking_day_boundary_is_not_started()
    {
        var result = TrainingStateResolver.Resolve(
            new RehabPlanEntity
            {
                IsComplete = 0,
                BookingTime = new DateTime(2026, 7, 27, 8, 30, 0),
                StartTime = new DateTime(2026, 7, 26, 15, 0, 0)
            },
            AtChina(2026, 7, 27, 23),
            China);

        Assert.Equal("not_started", result.State);
    }

    [Fact]
    public void Incomplete_after_booking_day_boundary_is_not_completed()
    {
        var result = TrainingStateResolver.Resolve(
            new RehabPlanEntity
            {
                IsComplete = 0,
                BookingTime = new DateTime(2026, 7, 27, 8, 30, 0),
                StartTime = new DateTime(2026, 7, 26, 15, 0, 0)
            },
            AtChina(2026, 7, 28, 0),
            China);

        Assert.Equal("not_completed", result.State);
    }

    [Fact]
    public void Current_start_time_does_not_prove_execution()
    {
        var result = TrainingStateResolver.Resolve(
            new RehabPlanEntity
            {
                IsComplete = 0,
                StartTime = new DateTime(2026, 7, 26, 15, 0, 0)
            },
            AtChina(2026, 7, 28, 0),
            China);

        Assert.Equal("execution_unknown", result.State);
        Assert.Contains(
            result.Issues,
            x => x.Code == IReGoReasonCodes.StateEvidenceConflict);
    }

    [Fact]
    public void Incomplete_with_execution_evidence_remains_execution_unknown()
    {
        var result = TrainingStateResolver.Resolve(
            new RehabPlanEntity
            {
                IsComplete = 0,
                BookingTime = new DateTime(2026, 7, 26, 15, 0, 0)
            },
            AtChina(2026, 7, 28, 0),
            China,
            hasExecutionEvidence: true);

        Assert.Equal("execution_unknown", result.State);
        Assert.Contains(
            result.Issues,
            x => x.Code == IReGoReasonCodes.CompletionFlagEvidenceConflict);
    }

    private static DateTimeOffset AtChina(
        int year,
        int month,
        int day,
        int hour)
    {
        var value = new DateTime(year, month, day, hour, 0, 0);
        return new DateTimeOffset(value, China.GetUtcOffset(value));
    }
}
