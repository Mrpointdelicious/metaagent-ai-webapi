using AI_WebApi.IServices.AITools;
using AI_WebApi.Model.AITools;
using AI_WebApi.Model.Domain.IReGo;
using AI_WebApi.Model.Domain.IReTour;
using AI_WebApi.Model.DTOs.AITools;
using AI_WebApi.Model.Entities;
using Microsoft.Extensions.Options;

namespace AI_WebApi.Services.AITools.IReTour;

/// <summary>
/// Longitudinal analysis over one specific activity: deterministic windows,
/// same-definition metric series with missing points preserved, plain
/// numeric direction only. No efficacy or diagnostic claims (v1).
/// </summary>
public sealed class IReTourLongitudinalAnalysisService : IIReTourLongitudinalAnalysisService
{
    private const int MaxWindowSessions = 20;
    private const int MaxDateRangeDays = 180;

    private readonly IIReTourPatientEvaluationService _evaluation;
    private readonly IIReTourSessionRefProvider _refs;
    private readonly IReTourToolOptions _options;

    public IReTourLongitudinalAnalysisService(
        IIReTourPatientEvaluationService evaluation,
        IIReTourSessionRefProvider refs,
        IOptions<IReTourToolOptions> options)
    {
        _evaluation = evaluation;
        _refs = refs;
        _options = options.Value;
    }

    public async Task<ToolResponseDto<IReTourLongitudinalAnalysisDto>> GetAsync(
        TrustedPatientContext context,
        GetIReTourLongitudinalAnalysisRequestDto request,
        string requestId,
        CancellationToken cancellationToken)
    {
        var evaluation = await _evaluation.EvaluateAsync(context, requestId, cancellationToken);
        if (!evaluation.PatientExists || !evaluation.BindingVerified)
        {
            return ToolResponseDto<IReTourLongitudinalAnalysisDto>.Create(
                "get_iretour_longitudinal_analysis",
                requestId,
                ToolStatuses.Unavailable,
                null,
                new[] { IReTourReasonCodes.PatientBindingNotFound },
                IReTourToolMeta.Build(_options));
        }

        var eligible = evaluation.Sessions
            .Where(x =>
                x.Visible &&
                x.CanonicalCode == request.ActivityScope &&
                x.ResultState == IReTourResultStates.SummaryAvailable &&
                x.ExecutionState == IReTourExecutionStates.Completed)
            .ToList();
        if (eligible.Count == 0)
        {
            return ToolResponseDto<IReTourLongitudinalAnalysisDto>.Create(
                "get_iretour_longitudinal_analysis",
                requestId,
                ToolStatuses.Unavailable,
                null,
                new[] { IReTourReasonCodes.NoIReTourData },
                IReTourToolMeta.Build(_options));
        }

        IReadOnlyList<IReTourEvaluatedSession> window;
        try
        {
            window = SelectWindow(eligible, request);
        }
        catch (ToolWindowException exception)
        {
            return ToolResponseDto<IReTourLongitudinalAnalysisDto>.Create(
                "get_iretour_longitudinal_analysis",
                requestId,
                ToolStatuses.Unavailable,
                null,
                new[] { exception.ReasonCode },
                IReTourToolMeta.Build(_options));
        }

        if (window.Count < 2)
        {
            return ToolResponseDto<IReTourLongitudinalAnalysisDto>.Create(
                "get_iretour_longitudinal_analysis",
                requestId,
                ToolStatuses.Unavailable,
                null,
                new[] { IReTourReasonCodes.LongitudinalRequiresTwoSessions },
                IReTourToolMeta.Build(_options));
        }

        // Parse whitelisted metrics per window session; keep the window order.
        var metricsByCode = new Dictionary<string, List<(int Ordinal, decimal? Value)>>(
            StringComparer.Ordinal);
        var definitions = new Dictionary<string, IReTourMetricDefinition>(StringComparer.Ordinal);
        var sessionDtos = new List<IReTourLongitudinalSessionDto>(window.Count);
        for (var index = 0; index < window.Count; index++)
        {
            var session = window[index];
            sessionDtos.Add(new IReTourLongitudinalSessionDto
            {
                Ordinal = index + 1,
                SessionRef = _refs.Issue(session),
                SessionTime = session.SortTime,
                CanonicalCode = session.CanonicalCode,
                TrainingState = session.ExecutionState,
                ResultState = session.ResultState
            });

            var row = VerifiedSummaryRow(evaluation, session);
            if (row?.Details == null)
            {
                continue;
            }

            var parser = IReTourSummaryParserRegistry.Resolve(session.ItemId);
            if (parser == null)
            {
                continue;
            }

            var parsed = IReTourSummaryExtraction.Parse(row.Details, parser);
            foreach (var metric in parsed.Metrics)
            {
                if (!metricsByCode.TryGetValue(metric.MetricCode, out var points))
                {
                    points = new List<(int, decimal?)>();
                    metricsByCode[metric.MetricCode] = points;
                }

                points.Add((
                    index + 1,
                    metric.ValueStatus == "valid" ? metric.NormalizedValue : null));
                definitions.TryAdd(
                    metric.MetricCode,
                    parser.Metrics.First(m => m.MetricCode == metric.MetricCode));
            }
        }

        var series = new List<IReTourMetricSeriesDto>();
        foreach (var (code, points) in metricsByCode.OrderBy(x => x.Key))
        {
            // Missing intermediate sessions keep their ordinal position with
            // value=null; series without two valid points are not comparable.
            var valid = points.Where(x => x.Value.HasValue).ToList();
            if (valid.Count < 2)
            {
                continue;
            }

            var definition = definitions[code];
            var first = valid[0].Value!.Value;
            var last = valid[^1].Value!.Value;
            var direction = Direction(valid.Select(x => x.Value!.Value).ToList());
            series.Add(new IReTourMetricSeriesDto
            {
                MetricCode = code,
                DisplayName = definition.DisplayName,
                Unit = definition.Unit,
                Points = points.Select(x => new IReTourMetricPointDto
                {
                    Ordinal = x.Ordinal,
                    SessionRef = sessionDtos[x.Ordinal - 1].SessionRef,
                    SessionTime = sessionDtos[x.Ordinal - 1].SessionTime,
                    Value = x.Value,
                    ValueStatus = x.Value.HasValue ? "valid" : "missing"
                }).ToList(),
                FirstValue = first,
                LastValue = last,
                AbsoluteChange = last - first,
                RelativeChange = first != 0 ? (last - first) / Math.Abs(first) : null,
                Direction = direction,
                InterpretationStatus = "not_evaluated",
                RegistryVersion = IReTourContract.MetricRegistryVersion
            });
        }

        if (series.Count == 0)
        {
            return ToolResponseDto<IReTourLongitudinalAnalysisDto>.Create(
                "get_iretour_longitudinal_analysis",
                requestId,
                ToolStatuses.Unavailable,
                null,
                new[] { IReTourReasonCodes.NoComparableMetric },
                IReTourToolMeta.Build(_options));
        }

        var started = sessionDtos.Min(x => x.SessionTime);
        var ended = sessionDtos.Max(x => x.SessionTime);
        var data = new IReTourLongitudinalAnalysisDto
        {
            PatientRef = context.PatientRef,
            Window = new IReTourLongitudinalWindowDto
            {
                SelectionMode = request.SelectionMode,
                RequestedCount = request.ReportCount,
                ResolvedCount = window.Count,
                ComparableCount = series.Count,
                StartedAt = started,
                EndedAt = ended,
                IsContiguous = request.SelectionMode == "latest_count" ||
                               request.SelectionMode == "recent_ordinal_range"
            },
            Sessions = sessionDtos,
            MetricSeries = series,
            Quality = null,
            AnswerableQuestions = new[]
            {
                "最近几次同类训练有哪些可比数据？",
                "某个指标的原始数值如何变化？",
                "哪些训练因为缺失或质量问题没有参加比较？"
            }
        };

        return ToolResponseDto<IReTourLongitudinalAnalysisDto>.Create(
            "get_iretour_longitudinal_analysis",
            requestId,
            ToolStatuses.Success,
            data,
            null,
            IReTourToolMeta.Build(_options));
    }

    private static IReadOnlyList<IReTourEvaluatedSession> SelectWindow(
        IReadOnlyList<IReTourEvaluatedSession> eligible,
        GetIReTourLongitudinalAnalysisRequestDto request)
    {
        switch (request.SelectionMode)
        {
            case "latest_count":
                return eligible.Take(Math.Min(request.ReportCount, MaxWindowSessions)).ToList();
            case "recent_ordinal_range":
            {
                var start = request.StartOrdinal!.Value;
                var end = request.EndOrdinal!.Value;
                var span = Math.Abs(end - start) + 1;
                if (span > MaxWindowSessions)
                {
                    throw new ToolWindowException(IReTourReasonCodes.LongitudinalWindowTooLarge);
                }

                var from = Math.Min(start, end) - 1;
                return eligible.Skip(from).Take(span).ToList();
            }
            case "date_range":
            {
                var start = request.StartDate!.Value;
                var end = request.EndDate!.Value;
                if ((end - start).TotalDays > MaxDateRangeDays)
                {
                    throw new ToolWindowException(IReTourReasonCodes.LongitudinalWindowTooLarge);
                }

                var matched = eligible
                    .Where(x => x.SortTime >= start && x.SortTime <= end)
                    .ToList();
                if (matched.Count > MaxWindowSessions)
                {
                    throw new ToolWindowException(IReTourReasonCodes.LongitudinalWindowTooLarge);
                }

                return matched;
            }
            default:
                throw new ToolWindowException(IReTourReasonCodes.InvalidRequest);
        }
    }

    private static IReTourSummaryReportEntity? VerifiedSummaryRow(
        IReTourEvaluationResult evaluation,
        IReTourEvaluatedSession session)
    {
        if (!evaluation.SummaryReportsByWalk.TryGetValue(session.WalkId, out var rows))
        {
            return null;
        }

        var matching = rows.Where(x => x.ItemId == session.ItemId).ToList();
        return matching.Count == 1 ? matching[0] : null;
    }

    private static string Direction(IReadOnlyList<decimal> values)
    {
        var increasing = true;
        var decreasing = true;
        for (var index = 1; index < values.Count; index++)
        {
            if (values[index] > values[index - 1])
            {
                decreasing = false;
            }
            else if (values[index] < values[index - 1])
            {
                increasing = false;
            }
        }

        if (increasing && decreasing)
        {
            return "unchanged";
        }

        if (increasing)
        {
            return "increased";
        }

        return decreasing ? "decreased" : "fluctuating";
    }

    private sealed class ToolWindowException : Exception
    {
        public ToolWindowException(string reasonCode)
        {
            ReasonCode = reasonCode;
        }

        public string ReasonCode { get; }
    }
}
