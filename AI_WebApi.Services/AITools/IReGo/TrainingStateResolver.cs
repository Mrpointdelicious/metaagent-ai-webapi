using AI_WebApi.Model.AITools;
using AI_WebApi.Model.Domain.IReGo;
using AI_WebApi.Model.DTOs.AITools;
using AI_WebApi.Model.Entities;

namespace AI_WebApi.Services.AITools.IReGo;

/// <summary>
/// Resolves robotdb_main plan state. rehabplan.StartTime is scheduled_at and
/// cannot prove that execution started.
/// </summary>
public static class TrainingStateResolver
{
    public static TrainingStateResult Resolve(
        RehabPlanEntity plan,
        DateTimeOffset now,
        TimeZoneInfo timeZone,
        bool hasExecutionEvidence = false)
    {
        var issues = new List<ToolIssueDto>();

        if (plan.IsComplete == 1)
        {
            return new TrainingStateResult
            {
                State = "completed",
                Confidence = "high",
                Issues = issues
            };
        }

        if (plan.IsComplete != 0)
        {
            issues.Add(Conflict("IsComplete is neither 0 nor 1."));
            return Unknown(issues);
        }

        if (hasExecutionEvidence)
        {
            issues.Add(new ToolIssueDto
            {
                Code = IReGoReasonCodes.CompletionFlagEvidenceConflict,
                Severity = "warning",
                Scope = "completion_state",
                Action = "retain_execution_unknown",
                Detail =
                    "CompleteStatus=0 conflicts with a summary report or non-empty raw data."
            });
            return Unknown(issues);
        }

        // BookingTime is the normalized robotdb_main scheduled_at value.
        if (!plan.BookingTime.HasValue)
        {
            issues.Add(Conflict("scheduled_at is absent and no reliable execution timestamp exists."));
            return Unknown(issues);
        }

        var bookingLocal = DateTime.SpecifyKind(plan.BookingTime.Value, DateTimeKind.Unspecified);
        var endOfBookingDayLocal = bookingLocal.Date.AddDays(1).AddTicks(-1);
        var boundary = new DateTimeOffset(
            endOfBookingDayLocal,
            timeZone.GetUtcOffset(endOfBookingDayLocal));

        return new TrainingStateResult
        {
            State = now <= boundary ? "not_started" : "not_completed",
            Confidence = "medium",
            Issues = issues
        };
    }

    private static ToolIssueDto Conflict(string detail) => new()
    {
        Code = IReGoReasonCodes.StateEvidenceConflict,
        Severity = "warning",
        Scope = "time",
        Action = "exclude_from_trend",
        Detail = detail
    };

    private static TrainingStateResult Unknown(IReadOnlyList<ToolIssueDto> issues) => new()
    {
        State = "execution_unknown",
        Confidence = "low",
        Issues = issues
    };
}
