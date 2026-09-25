using AI_WebApi.IServices.AITools;
using AI_WebApi.Model.AITools;
using AI_WebApi.Model.Domain.IReGo;
using AI_WebApi.Model.Domain.IReTour;
using AI_WebApi.Model.DTOs.AITools;
using AI_WebApi.Model.Entities;
using AI_WebApi.Repository.AITools;

namespace AI_WebApi.Services.AITools.IReTour;

/// <summary>
/// Shared evaluation pipeline for the four IReTour read tools: verified
/// patient binding, device gate (DeviceType=6), item registry, time/state
/// semantics, report-link classification and the patient-visible gate.
/// Summary JSON is NOT parsed here; history stays light and only the session
/// and longitudinal services load whitelisted metrics on demand.
/// </summary>
public sealed class IReTourPatientEvaluationService : IIReTourPatientEvaluationService
{
    private readonly IIReTourDataRepository _repository;
    private readonly IReTourTimeSemanticResolver _timeResolver;
    private readonly IReTourTrainingStateResolver _stateResolver;
    private readonly IReTourReportLinkResolver _linkResolver;

    public IReTourPatientEvaluationService(
        IIReTourDataRepository repository,
        IReTourTimeSemanticResolver timeResolver,
        IReTourTrainingStateResolver stateResolver,
        IReTourReportLinkResolver linkResolver)
    {
        _repository = repository;
        _timeResolver = timeResolver;
        _stateResolver = stateResolver;
        _linkResolver = linkResolver;
    }

    public async Task<IReTourEvaluationResult> EvaluateAsync(
        TrustedPatientContext context,
        string requestId,
        CancellationToken cancellationToken)
    {
        var data = await _repository.LoadPatientDataAsync(context, cancellationToken);
        var summaryByWalk = data.SummaryReports
            .GroupBy(x => x.PlanId)
            .ToDictionary(x => x.Key, x => (IReadOnlyList<IReTourSummaryReportEntity>)
                x.ToList());

        if (data.Patient == null || !data.Binding.Verified)
        {
            return new IReTourEvaluationResult
            {
                PatientExists = data.Patient != null,
                BindingVerified = data.Binding.Verified,
                SummaryReportsByWalk = summaryByWalk
            };
        }

        var sessions = new List<IReTourEvaluatedSession>(data.Walks.Count);
        foreach (var walk in data.Walks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            sessions.Add(EvaluateOne(data, walk));
        }

        return new IReTourEvaluationResult
        {
            PatientExists = true,
            BindingVerified = true,
            Sessions = sessions
                .OrderByDescending(x => x.SortTime)
                .ThenByDescending(x => x.WalkId)
                .ToList(),
            SummaryReportsByWalk = summaryByWalk
        };
    }

    private IReTourEvaluatedSession EvaluateOne(
        IReTourRawDataSet data,
        IReTourWalkEntity walk)
    {
        var issues = new List<ToolIssueDto>();

        var deviceVerified =
            data.Devices.TryGetValue(walk.DeviceId, out var device) &&
            device.DeviceType == IReTourContract.RequiredDeviceType;
        if (!deviceVerified)
        {
            issues.Add(new ToolIssueDto
            {
                Code = IReTourReasonCodes.DeviceReferenceInvalid,
                Severity = "error",
                Scope = "device",
                Action = "exclude_from_patient_view",
                PatientMessage = "该训练记录无法确认设备归属。"
            });
        }

        var item = IReTourItemRegistry.Resolve(walk.ItemId);
        if (item == null)
        {
            issues.Add(new ToolIssueDto
            {
                Code = IReTourReasonCodes.ItemUnknown,
                Severity = "error",
                Scope = "activity",
                Action = "exclude_from_patient_view",
                PatientMessage = "该训练类型暂未确认。"
            });
        }

        var time = _timeResolver.Resolve(walk);
        issues.AddRange(time.Issues);
        var (state, confidence) = _stateResolver.Resolve(time);

        var link = _linkResolver.Resolve(
            walk,
            data.SummaryReports,
            data.RawReports,
            data.CognitiveReports);
        if (link.SummaryConflict)
        {
            issues.Add(new ToolIssueDto
            {
                Code = IReTourReasonCodes.ReportLinkConflict,
                Severity = "warning",
                Scope = "report_link",
                Action = "exclude_summary",
                PatientMessage = "本次训练存在多条不一致的汇总记录。"
            });
        }

        if (walk.Duration is < 0 ||
            walk.Duration == 0 && time.ExecutionStartedAt.HasValue)
        {
            issues.Add(new ToolIssueDto
            {
                Code = IReTourReasonCodes.DurationInvalid,
                Severity = "warning",
                Scope = "duration",
                Action = "exclude_value",
                PatientMessage = "本次训练时长记录异常。"
            });
        }

        var resultState = link.SummaryConflict
            ? IReTourResultStates.ResultConflicting
            : link.SummaryVerified
                ? IReTourResultStates.SummaryAvailable
                : link.HasRawWalk || link.HasCognitive
                    ? IReTourResultStates.RawOnly
                    : IReTourResultStates.NoResult;

        var rawSourceTypes = new List<string>();
        if (link.RawLinkVerified)
        {
            rawSourceTypes.Add("walkreport");
        }

        if (link.CognitiveLinkVerified)
        {
            rawSourceTypes.Add("cogwalkreport");
        }

        return new IReTourEvaluatedSession
        {
            CanonicalSessionKey = $"walk:{walk.Id}",
            WalkId = walk.Id,
            PatientId = data.Patient!.Id,
            BusinessId = data.Patient.BusinessId,
            PatientBindingNamespace = data.Binding.SourceUserNamespace,
            PatientBindingVersion = data.Binding.BindingVersion,
            ItemId = walk.ItemId,
            CanonicalCode = item?.CanonicalCode ?? IReTourItemRegistry.UnknownCode,
            SchemaFamily = item?.SchemaFamily ?? "unknown",
            DisplayName = item?.DisplayName ?? "未确认训练类型",
            DeviceVerified = deviceVerified,
            Time = time,
            ExecutionState = state,
            ResultState = resultState,
            DurationSeconds = walk.Duration is >= 0 ? walk.Duration : null,
            Issues = issues,
            RawEvidenceAvailable = rawSourceTypes.Count > 0,
            RawSourceTypes = rawSourceTypes,
            SourceWalkUserId = walk.UserId
        };
    }
}
