using System.Globalization;
using AI_WebApi.Model.AITools;
using AI_WebApi.Model.Domain.IReTour;
using AI_WebApi.Model.DTOs.AITools;
using AI_WebApi.Model.Entities;

namespace AI_WebApi.Services.AITools.IReTour;

/// <summary>
/// Parses the merged-schema string time columns into standard ISO offsets.
/// Sentinel values (0001/01/01 ...) become null with a time-invalid issue;
/// start later than end becomes a time-order issue. Raw strings stay in the
/// audit layer only and are never returned.
/// </summary>
public sealed class IReTourTimeSemanticResolver
{
    private static readonly string[] Formats =
    {
        "yyyy/MM/dd HH:mm:ss",
        "yyyy-MM-dd HH:mm:ss",
        "yyyy/MM/dd HH:mm:ss.fff",
        "yyyy-MM-dd HH:mm:ss.fff"
    };

    private const string SentinelPrefix = "0001/";

    public IReTourParsedTimeSet Resolve(IReTourWalkEntity walk)
    {
        var issues = new List<ToolIssueDto>();
        var started = Parse(walk.StartTime, issues, "execution_started_at");
        var ended = Parse(walk.EndTime, issues, "execution_ended_at");
        var created = Parse(walk.CreateTime, issues, "source_record_created_at");
        var parsed = Parse(walk.ParseTime, issues, "source_parsed_at");

        if (started.HasValue && ended.HasValue && started > ended)
        {
            issues.Add(new ToolIssueDto
            {
                Code = IReTourReasonCodes.TimeOrderInvalid,
                Severity = "warning",
                Scope = "time",
                Action = "none",
                PatientMessage = "本次训练的开始和结束时间记录异常。"
            });
        }

        return new IReTourParsedTimeSet
        {
            ExecutionStartedAt = started,
            ExecutionEndedAt = ended,
            SourceRecordCreatedAt = created,
            SourceParsedAt = parsed,
            Issues = issues
        };
    }

    private static DateTimeOffset? Parse(
        string? raw,
        ICollection<ToolIssueDto> issues,
        string field)
    {
        if (string.IsNullOrWhiteSpace(raw) || raw.StartsWith(SentinelPrefix))
        {
            return null;
        }

        var normalized = raw.Trim();
        if (DateTimeOffset.TryParseExact(
                normalized,
                Formats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out var parsed) ||
            DateTimeOffset.TryParse(
                normalized,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeLocal,
                out parsed))
        {
            return parsed.ToOffset(TimeSpan.FromHours(8));
        }

        issues.Add(new ToolIssueDto
        {
            Code = IReTourReasonCodes.TimeInvalid,
            Severity = "warning",
            Scope = $"time:{field}",
            Action = "none",
            PatientMessage = "部分训练时间记录无法确认。"
        });
        return null;
    }
}

/// <summary>
/// Evidence-first execution state: trusted start+end ordered pairs mean
/// completed; start without end means interrupted; no trusted execution
/// evidence means execution_unknown. status/gameStatus are raw evidence only
/// and never single-field state deciders.
/// </summary>
public sealed class IReTourTrainingStateResolver
{
    public (string State, string Confidence) Resolve(IReTourParsedTimeSet time)
    {
        var started = time.ExecutionStartedAt;
        var ended = time.ExecutionEndedAt;
        var ordered = started.HasValue &&
                      ended.HasValue &&
                      started <= ended &&
                      !time.Issues.Any(x => x.Code == IReTourReasonCodes.TimeOrderInvalid);

        if (started.HasValue && ended.HasValue && ordered)
        {
            return (IReTourExecutionStates.Completed, "high");
        }

        if (started.HasValue)
        {
            return (IReTourExecutionStates.Interrupted, "medium");
        }

        return (IReTourExecutionStates.ExecutionUnknown, "low");
    }
}

/// <summary>
/// Classifies the walk-to-report linkage from the bounded report row set.
/// v1: deterministic FK planId + matching itemId is verified; planId match
/// with itemId mismatch is a conflict; multiple rows for one (planId,itemId)
/// pair are flagged identical/conflicting duplicates. The optional
/// iretour_report_binding governance table, when present, overrides with
/// explicitly reviewed links.
/// </summary>
public sealed class IReTourReportLinkResolver
{
    public IReTourReportLink Resolve(
        IReTourWalkEntity walk,
        IReadOnlyList<IReTourSummaryReportEntity> summaries,
        IReadOnlyList<IReTourRawReportEntity> rawReports,
        IReadOnlyList<IReTourCognitiveReportEntity> cognitiveReports)
    {
        var summaryRows = summaries.Where(x => x.PlanId == walk.Id).ToList();
        var rawRows = rawReports.Where(x => x.PlanId == walk.Id).ToList();
        var cognitiveRows = cognitiveReports.Where(x => x.PlanId == walk.Id).ToList();

        bool summaryVerified = false;
        bool ambiguous = false;
        bool conflict = false;
        var itemMismatch = summaryRows.Any(x => x.ItemId != walk.ItemId);
        if (summaryRows.Count > 0)
        {
            var groups = summaryRows.GroupBy(x => x.ItemId).ToList();
            if (groups.Count > 1 || itemMismatch)
            {
                conflict = true;
            }
            else if (summaryRows.Count > 1)
            {
                // Same planId+itemId duplicates: identical payloads are a
                // warning, differing payloads are a conflict.
                var distinctContent = summaryRows
                    .Select(x => x.Details ?? string.Empty)
                    .Distinct(StringComparer.Ordinal)
                    .Count();
                if (distinctContent > 1)
                {
                    conflict = true;
                }
            }

            if (!conflict)
            {
                summaryVerified = true;
            }
        }

        var rawVerified = rawRows.Count > 0 && !rawRows.Any(x => x.ItemId != walk.ItemId);
        var cognitiveVerified =
            cognitiveRows.Count > 0 && !cognitiveRows.Any(x => x.ItemId != walk.ItemId);

        return new IReTourReportLink
        {
            SummaryVerified = summaryVerified,
            SummaryAmbiguous = ambiguous,
            SummaryConflict = conflict,
            SummaryRowCount = summaryRows.Count,
            HasRawWalk = rawRows.Count > 0,
            HasCognitive = cognitiveRows.Count > 0,
            RawLinkVerified = rawVerified,
            CognitiveLinkVerified = cognitiveVerified
        };
    }
}
