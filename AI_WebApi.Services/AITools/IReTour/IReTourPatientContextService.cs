using AI_WebApi.IServices.AITools;
using AI_WebApi.Model.AITools;
using AI_WebApi.Model.Domain.IReGo;
using AI_WebApi.Model.Domain.IReTour;
using AI_WebApi.Model.DTOs.AITools;
using Microsoft.Extensions.Options;

namespace AI_WebApi.Services.AITools.IReTour;

/// <summary>
/// IReTour context/overview: the entry tool that answers "what IReTour data
/// does this patient have and what can be queried next". Counterpart of the
/// IReGo multisource context tool, built on the shared evaluation pipeline
/// without parsing summary JSON.
/// </summary>
public sealed class IReTourPatientContextService : IIReTourPatientContextService
{
    private readonly IIReTourPatientEvaluationService _evaluation;
    private readonly IIReTourSessionRefProvider _refs;
    private readonly IReTourToolOptions _options;

    public IReTourPatientContextService(
        IIReTourPatientEvaluationService evaluation,
        IIReTourSessionRefProvider refs,
        IOptions<IReTourToolOptions> options)
    {
        _evaluation = evaluation;
        _refs = refs;
        _options = options.Value;
    }

    public async Task<ToolResponseDto<IReTourPatientContextDto>> GetAsync(
        TrustedPatientContext context,
        GetIReTourPatientContextRequestDto request,
        string requestId,
        CancellationToken cancellationToken)
    {
        var evaluation = await _evaluation.EvaluateAsync(context, requestId, cancellationToken);
        if (!evaluation.PatientExists || !evaluation.BindingVerified)
        {
            return ToolResponseDto<IReTourPatientContextDto>.Create(
                "get_iretour_patient_context",
                requestId,
                ToolStatuses.Unavailable,
                null,
                new[] { IReTourReasonCodes.PatientBindingNotFound },
                IReTourToolMeta.Build(_options));
        }

        var sessions = evaluation.Sessions;
        var visible = sessions.Where(x => x.Visible).ToList();
        var usable = visible
            .Where(x =>
                x.ResultState == IReTourResultStates.SummaryAvailable &&
                x.ExecutionState == IReTourExecutionStates.Completed)
            .ToList();
        var latest = usable.FirstOrDefault();

        var activitySummary = visible
            .GroupBy(x => x.CanonicalCode)
            .Select(group => new IReTourActivityCountDto
            {
                CanonicalCode = group.Key,
                DisplayName = group.First().DisplayName,
                SessionCount = group.Count(),
                UsableSessionCount = group.Count(x => usable.Contains(x))
            })
            .OrderByDescending(x => x.SessionCount)
            .ToList();

        var recentSessions = new List<IReTourRecentSessionDto>();
        var ordinal = 0;
        foreach (var session in visible.Take(_options.MaxRecentSessions))
        {
            ordinal++;
            recentSessions.Add(new IReTourRecentSessionDto
            {
                Ordinal = ordinal,
                SessionRef = _refs.Issue(session),
                SessionTime = session.SortTime,
                Activity = new IReTourActivityBriefDto
                {
                    ItemId = session.ItemId,
                    CanonicalCode = session.CanonicalCode,
                    DisplayName = session.DisplayName,
                    RegistryVersion = IReTourContract.ItemRegistryVersion
                },
                TrainingState = session.ExecutionState,
                ResultState = session.ResultState,
                DetailAvailable = session.ResultState is IReTourResultStates.SummaryAvailable or
                    IReTourResultStates.RawOnly
            });
        }

        var blockedReasons = new List<string>();
        if (latest == null)
        {
            blockedReasons.Add(IReTourReasonCodes.NoUsableSession);
        }

        var allIssues = visible
            .SelectMany(x => x.Issues)
            .DistinctBy(x => x.Code)
            .Take(10)
            .ToList();

        var domain = new IReTourContextDomainDto
        {
            Availability = sessions.Count > 0 ? "available" : "unavailable",
            DataState = sessions.Count > 0 ? "available" : "empty",
            Counts = new IReTourContextCountsDto
            {
                SessionCount = sessions.Count,
                SummaryAvailableCount = visible.Count(x =>
                    x.ResultState == IReTourResultStates.SummaryAvailable),
                RawOnlyCount = visible.Count(x => x.ResultState == IReTourResultStates.RawOnly),
                NoResultCount = visible.Count(x =>
                    x.ResultState is IReTourResultStates.NoResult or
                        IReTourResultStates.ResultUnusable),
                UsableSessionCount = usable.Count,
                QualityBlockedCount = sessions.Count(x => !x.Visible)
            },
            ActivitySummary = activitySummary,
            RecentSessions = recentSessions,
            Latest = latest == null
                ? null
                : new IReTourLatestSessionDto
                {
                    SessionRef = _refs.Issue(latest),
                    SessionTime = latest.SortTime,
                    TrainingState = latest.ExecutionState,
                    ResultState = latest.ResultState
                },
            QueryCapabilities = new IReTourQueryCapabilitiesDto
            {
                LatestUsable = latest != null,
                BySessionRef = visible.Count > 0,
                Longitudinal = usable.Count >= 2,
                BlockedReasonCodes = blockedReasons
            },
            QueryCapabilityDetails = new[]
            {
                new IReTourQueryCapabilityDto
                {
                    Capability = "latest_usable",
                    Available = latest != null,
                    Reason = latest == null ? IReTourReasonCodes.NoUsableSession : null
                },
                new IReTourQueryCapabilityDto
                {
                    Capability = "longitudinal",
                    Available = usable.Count >= 2,
                    Reason = usable.Count < 2
                        ? IReTourReasonCodes.LongitudinalRequiresTwoSessions
                        : null
                }
            },
            Quality = new IReTourSessionQualityDto
            {
                Usable = latest != null,
                IssueCount = allIssues.Count,
                Issues = allIssues
            }
        };

        var data = new IReTourPatientContextDto
        {
            PatientRef = context.PatientRef,
            IReTour = domain
        };
        return ToolResponseDto<IReTourPatientContextDto>.Create(
            "get_iretour_patient_context",
            requestId,
            ToolStatuses.Success,
            data,
            null,
            IReTourToolMeta.Build(_options));
    }
}
