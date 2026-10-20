using AI_WebApi.IServices.AITools;
using AI_WebApi.Model.AITools;
using AI_WebApi.Model.Domain.IReGo;
using AI_WebApi.Model.Domain.IReTour;
using AI_WebApi.Model.DTOs.AITools;
using AI_WebApi.Model.Entities;
using Microsoft.Extensions.Options;

namespace AI_WebApi.Services.AITools.IReTour;

/// <summary>
/// Single-session analysis: resolves one authorized session (latest_usable or
/// a tool-issued session_ref), then returns plan facts, execution/time facts,
/// whitelisted summary metrics, quality issues and raw-evidence presence.
/// Raw sensor arrays are never returned.
/// </summary>
public sealed class IReTourSessionAnalysisService : IIReTourSessionAnalysisService
{
    private readonly IIReTourPatientEvaluationService _evaluation;
    private readonly IIReTourSessionRefProvider _refs;
    private readonly IReTourToolOptions _options;

    public IReTourSessionAnalysisService(
        IIReTourPatientEvaluationService evaluation,
        IIReTourSessionRefProvider refs,
        IOptions<IReTourToolOptions> options)
    {
        _evaluation = evaluation;
        _refs = refs;
        _options = options.Value;
    }

    public async Task<ToolResponseDto<IReTourSessionAnalysisDto>> GetAsync(
        TrustedPatientContext context,
        GetIReTourSessionAnalysisRequestDto request,
        string requestId,
        CancellationToken cancellationToken)
    {
        var evaluation = await _evaluation.EvaluateAsync(context, requestId, cancellationToken);
        if (!evaluation.PatientExists || !evaluation.BindingVerified)
        {
            return ToolResponseDto<IReTourSessionAnalysisDto>.Create(
                "get_iretour_session_analysis",
                requestId,
                ToolStatuses.Unavailable,
                null,
                new[] { IReTourReasonCodes.PatientBindingNotFound },
                IReTourToolMeta.Build(_options));
        }

        IReTourEvaluatedSession? selected;
        if (request.Selector == "latest_usable")
        {
            selected = await ResolveLatestUsableAsync(evaluation, cancellationToken);
            if (selected == null)
            {
                return ToolResponseDto<IReTourSessionAnalysisDto>.Create(
                    "get_iretour_session_analysis",
                    requestId,
                    ToolStatuses.Unavailable,
                    null,
                    new[] { IReTourReasonCodes.NoUsableSession },
                    IReTourToolMeta.Build(_options));
            }
        }
        else
        {
            selected = _refs.Verify(request.SessionRef!, context.PatientId, evaluation.Sessions);
            if (selected == null || !selected.Visible)
            {
                return ToolResponseDto<IReTourSessionAnalysisDto>.Create(
                    "get_iretour_session_analysis",
                    requestId,
                    ToolStatuses.Failed,
                    null,
                    new[] { IReTourReasonCodes.SessionRefNotFound },
                    IReTourToolMeta.Build(_options));
            }
        }

        var summaryRow = IReTourSummarySelection.Verified(evaluation, selected);
        var metrics = new List<IReTourMetricFact>();
        var parameters = new List<IReTourPlanParameterDto>();
        var issues = selected.Issues.ToList();

        if (selected.ResultState == IReTourResultStates.SummaryAvailable &&
            summaryRow?.Details != null)
        {
            var parser = IReTourSummaryParserRegistry.Resolve(selected.ItemId);
            if (parser != null)
            {
                var parsed = IReTourSummaryExtraction.Parse(summaryRow.Details, parser);
                metrics.AddRange(parsed.Metrics);
                parameters.AddRange(parsed.Parameters);
                issues.AddRange(parsed.Issues);
            }
            else
            {
                issues.Add(new ToolIssueDto
                {
                    Code = IReTourReasonCodes.ItemUnknown,
                    Severity = "error",
                    Scope = "activity",
                    Action = "exclude_metrics",
                    PatientMessage = "该训练类型暂未确认。"
                });
            }
        }

        var data = new IReTourSessionAnalysisDto
        {
            Session = new IReTourSessionIdentityDto
            {
                SessionRef = _refs.Issue(selected),
                PatientRef = context.PatientRef
            },
            Time = new IReTourSessionTimeDto
            {
                ExecutionStartedAt = selected.Time.ExecutionStartedAt,
                ExecutionEndedAt = selected.Time.ExecutionEndedAt,
                SourceRecordCreatedAt = selected.Time.SourceRecordCreatedAt,
                SourceParsedAt = selected.Time.SourceParsedAt,
                TrainingState = selected.ExecutionState,
                StateConfidence = StateConfidence(selected),
            },
            Activity = new IReTourActivityBriefDto
            {
                ItemId = selected.ItemId,
                CanonicalCode = selected.CanonicalCode,
                DisplayName = selected.DisplayName,
                RegistryVersion = IReTourContract.ItemRegistryVersion
            },
            Plan = new IReTourSessionPlanDto
            {
                DurationSeconds = selected.DurationSeconds,
                Parameters = parameters
            },
            Result = new IReTourSessionResultDto
            {
                ResultState = selected.ResultState,
                Metrics = metrics.Select(x => new IReTourMetricDto
                {
                    MetricCode = x.MetricCode,
                    DisplayName = x.DisplayName,
                    RawValue = x.RawValue,
                    NormalizedValue = x.NormalizedValue,
                    Unit = x.Unit,
                    SourceField = x.SourceField,
                    UnitStatus = x.Unit == null ? "unconfirmed" : "confirmed",
                    ValueStatus = x.ValueStatus,
                    RegistryVersion = IReTourContract.MetricRegistryVersion
                }).ToList(),
                RawEvidence = new IReTourRawEvidenceDto
                {
                    Available = selected.RawEvidenceAvailable,
                    SourceTypes = selected.RawSourceTypes,
                    ReturnedToModel = false
                }
            },
            Quality = new IReTourSessionQualityDto
            {
                Usable = selected.ExecutionState == IReTourExecutionStates.Completed &&
                    metrics.Any(x => x.ValueStatus == "valid" && x.NormalizedValue.HasValue) &&
                    !issues.Any(x => x.Severity == "error"),
                IssueCount = issues.Count,
                Issues = issues
            },
            Sources = new[]
            {
                new IReTourSourceFactDto
                {
                    SourceTable = "walkreportdetails",
                    LinkMethod = summaryRow == null ? "none" : "deterministic_fk",
                    LinkStatus = summaryRow == null ? "absent" : "verified"
                }
            }
        };

        data.Interpretation = IReTourReadingBuilder.Single(data);
        data.SourceSnapshot = IReTourReportContentFingerprint.SingleSourceSnapshot(data);
        return ToolResponseDto<IReTourSessionAnalysisDto>.Create(
            "get_iretour_session_analysis",
            requestId,
            data.Quality.Usable ? ToolStatuses.Success : ToolStatuses.Partial,
            data,
            null,
            IReTourToolMeta.Build(_options));
    }

    private async Task<IReTourEvaluatedSession?> ResolveLatestUsableAsync(
        IReTourEvaluationResult evaluation,
        CancellationToken cancellationToken)
    {
        foreach (var session in evaluation.Sessions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!session.Visible ||
                session.ResultState != IReTourResultStates.SummaryAvailable ||
                session.ExecutionState != IReTourExecutionStates.Completed)
            {
                continue;
            }

            var row = IReTourSummarySelection.Verified(evaluation, session);
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
            if (parsed.Issues.Any(x => x.Severity == "error") ||
                !parsed.Metrics.Any(x => x.ValueStatus == "valid" && x.NormalizedValue.HasValue))
            {
                continue;
            }

            return session;
        }

        return null;
    }

    private static string StateConfidence(IReTourEvaluatedSession session) =>
        session.ExecutionState switch
        {
            IReTourExecutionStates.Completed => "high",
            IReTourExecutionStates.Interrupted => "medium",
            _ => "low"
        };
}
