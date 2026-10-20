using System.Security.Cryptography;
using System.Text;
using AI_WebApi.IServices.AITools;
using AI_WebApi.Model.AITools;
using AI_WebApi.Model.Domain.IReGo;
using AI_WebApi.Model.Domain.IReTour;
using AI_WebApi.Model.DTOs.AITools;
using Microsoft.Extensions.Options;

namespace AI_WebApi.Services.AITools.IReTour;

/// <summary>Resolve the history window before excluding values. Never backfill missing results.</summary>
public sealed class IReTourLongitudinalAnalysisService : IIReTourLongitudinalAnalysisService
{
    private readonly IIReTourPatientEvaluationService _evaluation;
    private readonly IIReTourSessionRefProvider _refs;
    private readonly IReTourToolOptions _options;
    public IReTourLongitudinalAnalysisService(IIReTourPatientEvaluationService evaluation,
        IIReTourSessionRefProvider refs, IOptions<IReTourToolOptions> options)
    { _evaluation = evaluation; _refs = refs; _options = options.Value; }

    public async Task<ToolResponseDto<IReTourLongitudinalAnalysisDto>> GetAsync(
        TrustedPatientContext context, GetIReTourLongitudinalAnalysisRequestDto request,
        string requestId, CancellationToken cancellationToken)
    {
        ToolResponseDto<IReTourLongitudinalAnalysisDto> Unavailable(string reason) =>
            ToolResponseDto<IReTourLongitudinalAnalysisDto>.Create("get_iretour_longitudinal_analysis", requestId,
                ToolStatuses.Unavailable, null, new[] { reason }, IReTourToolMeta.Build(_options), contractVersion: IReTourContract.Version);
        if (!Valid(request)) return Unavailable(IReTourReasonCodes.InvalidRequest);
        var evaluation = await _evaluation.EvaluateAsync(context, requestId, cancellationToken);
        if (!evaluation.PatientExists || !evaluation.BindingVerified) return Unavailable(IReTourReasonCodes.PatientBindingNotFound);
        var history = evaluation.Sessions.Where(s => s.Visible && s.CanonicalCode == request.ActivityScope)
            .OrderByDescending(s => s.SortTime).ThenByDescending(s => s.WalkId).ToArray();
        IEnumerable<IReTourEvaluatedSession> selected = request.SelectionMode switch
        {
            "latest_count" => history.Take(request.ReportCount),
            "recent_ordinal_range" => history.Skip(Math.Min(request.StartOrdinal!.Value, request.EndOrdinal!.Value) - 1)
                .Take(Math.Abs(request.StartOrdinal.Value - request.EndOrdinal.Value) + 1),
            _ => history.Where(s => s.SortTime >= request.StartDate && s.SortTime <= request.EndDate)
        };
        var window = selected.OrderBy(s => s.SortTime).ThenBy(s => s.WalkId).ToArray();
        if (window.Length > 20) return Unavailable(IReTourReasonCodes.LongitudinalWindowTooLarge);
        if (window.Length < 2) return Unavailable(IReTourReasonCodes.LongitudinalRequiresTwoSessions);
        var sessions = new List<IReTourLongitudinalSessionDto>();
        var issues = new List<ToolIssueDto>();
        var groups = new Dictionary<string, (IReTourMetricFact Fact, List<(int Ordinal, decimal? Value)> Points)>();
        for (var i = 0; i < window.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var session = window[i];
            var reasons = new List<string>();
            var row = IReTourSummarySelection.Verified(evaluation, session);
            var parser = IReTourSummaryParserRegistry.Resolve(session.ItemId);
            if (session.ExecutionState != IReTourExecutionStates.Completed) reasons.Add("IRETOUR_EXECUTION_INCOMPLETE");
            if (row?.Details == null) reasons.Add("IRETOUR_SUMMARY_UNAVAILABLE");
            if (parser == null) reasons.Add(IReTourReasonCodes.SchemaUnsupported);
            if (reasons.Count == 0)
            {
                var parsed = IReTourSummaryExtraction.Parse(row!.Details!, parser!);
                issues.AddRange(parsed.Issues);
                if (!parsed.Metrics.Any(m => m.ValueStatus == "valid" && m.NormalizedValue.HasValue))
                    reasons.Add("IRETOUR_NO_VALID_METRIC");
                // Alias provenance and visible settings must match; same activity is insufficient.
                var settings = string.Join(";", parsed.Parameters.OrderBy(p => p.Code).Select(p => p.Code + "=" + p.Value));
                foreach (var metric in parsed.Metrics)
                {
                    var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                        $"{session.CanonicalCode}|{parser!.SchemaFamily}|{metric.MetricCode}|{metric.SourceField}|{metric.Unit}|{settings}"))).ToLowerInvariant();
                    if (!groups.TryGetValue(key, out var group))
                    { group = (metric, new()); groups.Add(key, group); }
                    group.Points.Add((i + 1, metric.ValueStatus == "valid" ? metric.NormalizedValue : null));
                }
            }
            sessions.Add(new() { Ordinal = i + 1, SessionRef = _refs.Issue(session), SessionTime = session.SortTime,
                CanonicalCode = session.CanonicalCode, TrainingState = session.ExecutionState,
                ResultState = session.ResultState, ExclusionReasonCodes = reasons });
        }
        var series = new List<IReTourMetricSeriesDto>();
        foreach (var (key, group) in groups.OrderBy(g => g.Key))
        {
            var points = sessions.Select(s => new IReTourMetricPointDto
            {
                Ordinal = s.Ordinal, SessionRef = s.SessionRef, SessionTime = s.SessionTime,
                Value = group.Points.FirstOrDefault(p => p.Ordinal == s.Ordinal).Value,
                ValueStatus = group.Points.Any(p => p.Ordinal == s.Ordinal && p.Value.HasValue) ? "valid" : "missing"
            }).ToArray();
            var valid = points.Where(p => p.Value.HasValue).ToArray();
            var comparable = valid.Length >= 2;
            var first = comparable ? valid[0].Value : null;
            var last = comparable ? valid[^1].Value : null;
            series.Add(new()
            {
                MetricCode = group.Fact.MetricCode, DisplayName = group.Fact.DisplayName,
                SourceField = group.Fact.SourceField, ComparisonKey = key,
                Unit = group.Fact.Unit, UnitStatus = group.Fact.Unit == null ? "unconfirmed" : "confirmed",
                ComparisonStatus = comparable ? "descriptive_same_source_field" : "insufficient_points",
                Points = points, FirstValue = first, LastValue = last, AbsoluteChange = last - first,
                RelativeChange = first.HasValue && first != 0 ? (last - first) / Math.Abs(first.Value) : null,
                Direction = comparable ? Direction(valid.Select(p => p.Value!.Value).ToArray()) : "unavailable",
                RegistryVersion = IReTourContract.MetricRegistryVersion
            });
        }
        var excluded = sessions.Count(s => s.ExclusionReasonCodes.Count > 0);
        var comparableCount = series.Count(s => s.ComparisonStatus == "descriptive_same_source_field");
        var requested = request.SelectionMode == "date_range" ? window.Length : request.SelectionMode == "latest_count"
            ? request.ReportCount : Math.Abs(request.StartOrdinal!.Value - request.EndOrdinal!.Value) + 1;
        var data = new IReTourLongitudinalAnalysisDto
        {
            PatientRef = context.PatientRef, Sessions = sessions, MetricSeries = series,
            Issues = issues.GroupBy(x => (x.Code, x.Scope)).Select(g => g.First()).ToArray(),
            Window = new() { SelectionMode = request.SelectionMode, RequestedCount = requested, ResolvedCount = window.Length,
                ComparableCount = comparableCount, ExcludedSessionCount = excluded,
                StartedAt = sessions.Min(s => s.SessionTime), EndedAt = sessions.Max(s => s.SessionTime), IsContiguous = true },
            AnswerableQuestions = new[] { "所选记录的指标数值如何变化？", "哪些记录缺失或不能比较？" }
        };
        data.Interpretation = IReTourReadingBuilder.Trend(data);
        data.SourceSnapshot = IReTourReportContentFingerprint.LongitudinalSourceSnapshot(data);
        var reasonsOut = new List<string>();
        if (comparableCount == 0) reasonsOut.Add(IReTourReasonCodes.NoComparableMetric);
        if (excluded > 0) reasonsOut.Add("IRETOUR_WINDOW_HAS_EXCLUSIONS");
        if (window.Length < requested) reasonsOut.Add("IRETOUR_WINDOW_SHORTFALL");
        return ToolResponseDto<IReTourLongitudinalAnalysisDto>.Create("get_iretour_longitudinal_analysis", requestId,
            reasonsOut.Count > 0 ? ToolStatuses.Partial : ToolStatuses.Success, data, reasonsOut,
            IReTourToolMeta.Build(_options), contractVersion: IReTourContract.Version);
    }

    public static bool Valid(GetIReTourLongitudinalAnalysisRequestDto r) =>
        IReTourItemRegistry.All().Any(i => i.CanonicalCode == r.ActivityScope) && (r.SelectionMode switch
        {
            "latest_count" => r.ReportCount is >= 2 and <= 20 && r.StartOrdinal == null && r.EndOrdinal == null && r.StartDate == null && r.EndDate == null,
            "recent_ordinal_range" => r.StartOrdinal is >= 1 and <= 1000 && r.EndOrdinal is >= 1 and <= 1000 &&
                Math.Abs(r.StartOrdinal.Value - r.EndOrdinal.Value) + 1 is >= 2 and <= 20 && r.StartDate == null && r.EndDate == null,
            "date_range" => r.StartDate.HasValue && r.EndDate.HasValue && r.StartDate <= r.EndDate &&
                (r.EndDate.Value - r.StartDate.Value).TotalDays <= 180 && r.StartOrdinal == null && r.EndOrdinal == null,
            _ => false
        });

    private static string Direction(decimal[] values)
    {
        var up = values.Zip(values.Skip(1)).Any(p => p.Second > p.First);
        var down = values.Zip(values.Skip(1)).Any(p => p.Second < p.First);
        return up && down ? "fluctuating" : up ? "increased" : down ? "decreased" : "unchanged";
    }
}
