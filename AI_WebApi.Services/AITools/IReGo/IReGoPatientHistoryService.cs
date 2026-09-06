using AI_WebApi.IServices.AITools;
using AI_WebApi.Model.AITools;
using AI_WebApi.Model.Domain.IReGo;
using AI_WebApi.Model.DTOs.AITools;
using Microsoft.Extensions.Options;

namespace AI_WebApi.Services.AITools.IReGo;

/// <summary>
/// Read-only history directory. It discovers plans and effective report
/// revisions, but intentionally does not execute longitudinal evaluation.
/// </summary>
public sealed class IReGoPatientHistoryService : IIReGoPatientHistoryService
{
    private readonly IIReGoPatientEvaluationService _evaluationService;
    private readonly IIReGoSessionRefProvider _sessionRefProvider;
    private readonly IIReGoStrokeCapabilityMapper _capabilityMapper;
    private readonly IReGoToolOptions _options;

    public IReGoPatientHistoryService(
        IIReGoPatientEvaluationService evaluationService,
        IIReGoSessionRefProvider sessionRefProvider,
        IIReGoStrokeCapabilityMapper capabilityMapper,
        IOptions<IReGoToolOptions> options)
    {
        _evaluationService = evaluationService;
        _sessionRefProvider = sessionRefProvider;
        _capabilityMapper = capabilityMapper;
        _options = options.Value;
    }

    public async Task<ToolResponseDto<IReGoPatientHistoryDto>> GetAsync(
        TrustedPatientContext context,
        GetIReGoPatientHistoryRequestDto request,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        if (!_sessionRefProvider.IsConfigured)
        {
            return Failure(
                requestId,
                IReGoReasonCodes.SessionRefSigningKeyNotConfigured);
        }

        var evaluation = await _evaluationService.EvaluateAsync(
            context,
            cancellationToken);
        if (evaluation.Raw.Patient == null ||
            evaluation.Raw.Patient.IsDeleted == true)
        {
            return Failure(
                requestId,
                IReGoReasonCodes.PatientNotFound,
                evaluation.Watermark);
        }

        var tenantId = evaluation.Raw.Patient.BusinessId;
        var candidates = evaluation.Plans
            .Select(plan => BuildCandidate(
                plan,
                tenantId,
                context.PatientId))
            .Where(x => InRecordScope(x, request.RecordScope))
            .Where(x => InProjectScope(x, request.ProjectScope))
            .Where(x => InTrainingState(x, request.TrainingState))
            .OrderByDescending(x => x.SortTime)
            .ThenByDescending(x => x.PlanId)
            .ToArray();

        var totalItems = candidates.Length;
        var totalPages = totalItems == 0
            ? 0
            : (int)Math.Ceiling(totalItems / (double)request.PageSize);
        var pageItems = candidates
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select((x, index) => ToDto(
                x,
                (request.PageNumber - 1) * request.PageSize + index + 1))
            .ToArray();
        var summary = new IReGoHistorySummaryDto
        {
            PlanCount = evaluation.Plans.Count,
            ReportCount = evaluation.Plans.Count(x =>
                x.EffectiveSession != null),
            CompleteReportCount = evaluation.Plans.Count(x =>
                x.EffectiveSession?.Usable == true),
            IncompletePlanCount = evaluation.Plans.Count(x =>
                x.State.State == "not_completed"),
            FuturePlanCount = evaluation.Plans.Count(x =>
                x.State.State == "not_started"),
            VoidedPlanCount = evaluation.Plans.Count(x => x.IsVoided)
        };
        var data = new IReGoPatientHistoryDto
        {
            PatientRef = _sessionRefProvider.CreatePatient(
                tenantId,
                context.PatientId),
            Page = new IReGoHistoryPageDto
            {
                PageNumber = request.PageNumber,
                PageSize = request.PageSize,
                TotalItems = totalItems,
                TotalPages = totalPages,
                HasPrevious = request.PageNumber > 1 && totalItems > 0,
                HasNext = request.PageNumber < totalPages
            },
            Summary = summary,
            Items = pageItems,
            AnswerableQuestions = new[]
            {
                "我有哪些既往训练或计划？",
                "我还有没有未完整完成或尚未开始的计划？",
                "请继续查看下一页训练记录。",
                "请用某条稳定会话引用查询单次训练详情。"
            }
        };
        return ToolResponseDto<IReGoPatientHistoryDto>.Create(
            "get_irego_patient_history",
            requestId,
            ToolStatuses.Success,
            data,
            Array.Empty<string>(),
            Meta(evaluation.Watermark));
    }

    private HistoryCandidate BuildCandidate(
        IReGoEvaluatedPlan plan,
        int? tenantId,
        int patientId)
    {
        var session = plan.EffectiveSession;
        var reportBlocks = session == null
            ? Array.Empty<IReGoContentBlockDto>()
            : session.ParsedReport.DirectTraining
                .Concat(session.ParsedReport.Games)
                .Concat(session.ParsedReport.Assessments)
                .ToArray();
        var blocks = reportBlocks.Length > 0
            ? reportBlocks
            : plan.ParsedPlan.Blocks.ToArray();
        var capabilityNames = reportBlocks
            .SelectMany(x => x.Metrics)
            .Select(x => _capabilityMapper.Resolve(x.MetricCode))
            .Where(x => x != null)
            .Select(x => x!.Name)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();
        var sessionRef = session == null
            ? null
            : _sessionRefProvider.Create(
                tenantId,
                patientId,
                session.Plan.Id,
                session.Report.Id,
                session.ParsedReport.SchemaSignature);
        var sortTime = SessionSortTime(session, plan);
        return new HistoryCandidate
        {
            PlanId = plan.Plan.Id,
            RecordType = session == null ? "plan" : "report",
            PlanRef = _sessionRefProvider.CreatePlan(
                tenantId,
                patientId,
                plan.Plan.Id),
            SessionRef = sessionRef,
            SessionTime = ToChinaOffset(sortTime),
            SortTime = sortTime,
            TrainingState = plan.IsVoided ? "voided" : plan.State.State,
            ReportStatus = session == null
                ? "not_available"
                : session.Usable
                    ? "available"
                    : "not_eligible_for_analysis",
            ContentTypes = blocks
                .Select(x => x.RehabContent)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.Ordinal)
                .ToArray(),
            DisplayNames = blocks
                .Select(x => x.DisplayName)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.Ordinal)
                .ToArray(),
            SingleSession = session?.Usable == true,
            Longitudinal = session?.Usable == true,
            EligibleCapabilities = capabilityNames,
            PatientMessage = PatientMessage(plan, session)
        };
    }

    private static IReGoHistoryItemDto ToDto(
        HistoryCandidate item,
        int ordinal) => new()
        {
            Ordinal = ordinal,
            RecordType = item.RecordType,
            PlanRef = item.PlanRef,
            SessionRef = item.SessionRef,
            SessionTime = item.SessionTime,
            TrainingState = item.TrainingState,
            ReportStatus = item.ReportStatus,
            ContentTypes = item.ContentTypes,
            DisplayNames = item.DisplayNames,
            EvaluationAvailability = new IReGoHistoryEvaluationAvailabilityDto
            {
                SingleSession = item.SingleSession,
                Longitudinal = item.Longitudinal,
                EligibleCapabilities = item.EligibleCapabilities
            },
            PatientMessage = item.PatientMessage
        };

    private static bool InRecordScope(
        HistoryCandidate item,
        string scope) => scope switch
        {
            "reports" => item.RecordType == "report",
            "plans" => item.RecordType == "plan",
            _ => true
        };

    private static bool InProjectScope(
        HistoryCandidate item,
        string scope)
    {
        if (scope == "all")
        {
            return true;
        }
        return item.ContentTypes.Any(content => scope switch
        {
            "walking" => content is "walking" or "single_walking",
            "sit_stand" => content is "sit_stand" or "single_sit",
            "games" => content is "game" or "vr_game",
            "balance" => content is
                "balance_training" or "balance_assessment",
            _ => false
        });
    }

    private static bool InTrainingState(
        HistoryCandidate item,
        string state) => state == "all" || item.TrainingState == state;

    private static string PatientMessage(
        IReGoEvaluatedPlan plan,
        IReGoEvaluatedSession? session)
    {
        if (plan.IsVoided)
        {
            return "该计划已作废，不作为待执行计划。";
        }
        if (session?.Usable == true)
        {
            return "该次训练报告可以继续查询和解读。";
        }
        if (session != null)
        {
            return "该次训练存在报告记录，但目前不能进行完整评价。";
        }
        return plan.State.State switch
        {
            "not_started" => "该计划尚未开始。",
            "not_completed" => "该计划没有完整完成。",
            _ => "该计划的执行状态暂时无法确认。"
        };
    }

    private ToolResponseDto<IReGoPatientHistoryDto> Failure(
        string requestId,
        string reasonCode,
        string? watermark = null) =>
        ToolResponseDto<IReGoPatientHistoryDto>.Create(
            "get_irego_patient_history",
            requestId,
            ToolStatuses.Failed,
            null,
            new[] { reasonCode },
            Meta(watermark));

    private ToolMetaDto Meta(string? watermark) => new()
    {
        GeneratedAt = DateTimeOffset.UtcNow,
        SourceCompleteness = _options.SourceCompleteness,
        Watermark = watermark
    };

    private static DateTime SessionSortTime(
        IReGoEvaluatedSession? session,
        IReGoEvaluatedPlan plan) =>
        ValidTime(session?.Report.CreateTime) ??
        ValidTime(plan.Plan.BookingTime) ??
        ValidTime(plan.Plan.CreateTime) ??
        DateTime.MinValue;

    private static DateTime? ValidTime(DateTime? value) =>
        value.HasValue && value.Value.Year > 1900 ? value.Value : null;

    private static DateTimeOffset? ToChinaOffset(DateTime value)
    {
        if (value == DateTime.MinValue)
        {
            return null;
        }
        var timeZone = IReGoPatientEvaluationService.ChinaTimeZone();
        var unspecified = DateTime.SpecifyKind(
            value,
            DateTimeKind.Unspecified);
        return new DateTimeOffset(
            unspecified,
            timeZone.GetUtcOffset(unspecified));
    }

    private sealed class HistoryCandidate
    {
        public int PlanId { get; init; }
        public string RecordType { get; init; } = string.Empty;
        public string PlanRef { get; init; } = string.Empty;
        public string? SessionRef { get; init; }
        public DateTimeOffset? SessionTime { get; init; }
        public DateTime SortTime { get; init; }
        public string TrainingState { get; init; } = string.Empty;
        public string ReportStatus { get; init; } = string.Empty;
        public IReadOnlyList<string> ContentTypes { get; init; } =
            Array.Empty<string>();
        public IReadOnlyList<string> DisplayNames { get; init; } =
            Array.Empty<string>();
        public bool SingleSession { get; init; }
        public bool Longitudinal { get; init; }
        public IReadOnlyList<string> EligibleCapabilities { get; init; } =
            Array.Empty<string>();
        public string PatientMessage { get; init; } = string.Empty;
    }
}
