using AI_WebApi.IServices.AITools;
using AI_WebApi.Model.AITools;
using AI_WebApi.Model.Domain.IReGo;
using AI_WebApi.Model.Domain.IReTour;
using AI_WebApi.Model.DTOs.AITools;
using Microsoft.Extensions.Options;

namespace AI_WebApi.Services.AITools.IReTour;

public static class IReTourToolMeta
{
    public static ToolMetaDto Build(IReTourToolOptions options, string? watermark = null) =>
        new()
        {
            SourceCompleteness = options.SourceCompleteness,
            Watermark = watermark,
            RegistryVersions = new RegistryVersionsDto
            {
                Contract = IReTourContract.Version,
                Device = IReTourContract.DeviceRegistryVersion,
                Schema = IReTourContract.SchemaRegistryVersion,
                Metric = IReTourContract.MetricRegistryVersion,
                State = IReTourContract.StateRegistryVersion
            }
        };
}

/// <summary>
/// History catalogue: discovers which IReTour sessions a patient has, whether
/// each has a readable result, and issues stable session_refs for gated
/// records only. It never parses summary JSON.
/// </summary>
public sealed class IReTourPatientHistoryService : IIReTourPatientHistoryService
{
    private readonly IIReTourPatientEvaluationService _evaluation;
    private readonly IIReTourSessionRefProvider _refs;
    private readonly IReTourToolOptions _options;

    public IReTourPatientHistoryService(
        IIReTourPatientEvaluationService evaluation,
        IIReTourSessionRefProvider refs,
        IOptions<IReTourToolOptions> options)
    {
        _evaluation = evaluation;
        _refs = refs;
        _options = options.Value;
    }

    public async Task<ToolResponseDto<IReTourPatientHistoryDto>> GetAsync(
        TrustedPatientContext context,
        GetIReTourPatientHistoryRequestDto request,
        string requestId,
        CancellationToken cancellationToken)
    {
        var evaluation = await _evaluation.EvaluateAsync(context, requestId, cancellationToken);
        if (!evaluation.PatientExists || !evaluation.BindingVerified)
        {
            return ToolResponseDto<IReTourPatientHistoryDto>.Create(
                "get_iretour_patient_history",
                requestId,
                ToolStatuses.Unavailable,
                null,
                new[] { IReTourReasonCodes.PatientBindingNotFound },
                IReTourToolMeta.Build(_options));
        }

        var sessions = evaluation.Sessions;
        if (sessions.Count == 0)
        {
            return ToolResponseDto<IReTourPatientHistoryDto>.Create(
                "get_iretour_patient_history",
                requestId,
                ToolStatuses.Unavailable,
                null,
                new[] { IReTourReasonCodes.NoIReTourData },
                IReTourToolMeta.Build(_options));
        }

        var filtered = sessions.Where(x => Matches(x, request)).ToList();

        var totalItems = filtered.Count;
        var totalPages = totalItems == 0
            ? 0
            : (int)Math.Ceiling((double)totalItems / request.PageSize);
        var pageItems = filtered
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToList();

        var summary = new IReTourHistorySummaryDto
        {
            SessionCount = filtered.Count,
            SummaryAvailableCount = filtered.Count(x =>
                x.ResultState == IReTourResultStates.SummaryAvailable),
            RawOnlyCount = filtered.Count(x => x.ResultState == IReTourResultStates.RawOnly),
            NoResultCount = filtered.Count(x =>
                x.ResultState is IReTourResultStates.NoResult or IReTourResultStates.ResultUnusable),
            QualityBlockedCount = filtered.Count(x => !x.Visible)
        };

        var items = new List<IReTourHistoryItemDto>(pageItems.Count);
        for (var index = 0; index < pageItems.Count; index++)
        {
            var session = pageItems[index];
            items.Add(new IReTourHistoryItemDto
            {
                Ordinal = (request.PageNumber - 1) * request.PageSize + index + 1,
                SessionRef = session.Visible ? _refs.Issue(session) : null,
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
                SingleSessionAvailable =
                    session.Visible &&
                    session.ResultState is IReTourResultStates.SummaryAvailable or
                        IReTourResultStates.RawOnly,
                LongitudinalAvailable =
                    session.Visible &&
                    session.ResultState == IReTourResultStates.SummaryAvailable &&
                    session.ExecutionState == IReTourExecutionStates.Completed,
                PatientMessage = Message(session)
            });
        }

        var data = new IReTourPatientHistoryDto
        {
            PatientRef = context.PatientRef,
            Page = new IReTourHistoryPageDto
            {
                PageNumber = request.PageNumber,
                PageSize = request.PageSize,
                TotalItems = totalItems,
                TotalPages = totalPages,
                HasPrevious = request.PageNumber > 1 && totalPages > 0,
                HasNext = request.PageNumber < totalPages
            },
            Summary = summary,
            Items = items,
            AnswerableQuestions = new[]
            {
                "我有哪些 IReTour 训练记录？",
                "哪几次训练有可读取的结果？",
                "请查看第几次训练的详情。"
            }
        };
        return ToolResponseDto<IReTourPatientHistoryDto>.Create(
            "get_iretour_patient_history",
            requestId,
            ToolStatuses.Success,
            data,
            null,
            IReTourToolMeta.Build(_options));
    }

    private static bool Matches(
        IReTourEvaluatedSession session,
        GetIReTourPatientHistoryRequestDto request)
    {
        if (request.RecordScope == "with_result" &&
            session.ResultState is IReTourResultStates.NoResult or
                IReTourResultStates.ResultUnusable)
        {
            return false;
        }

        if (request.RecordScope == "without_result" &&
            session.ResultState is not (IReTourResultStates.NoResult or
                IReTourResultStates.ResultUnusable))
        {
            return false;
        }

        if (request.ActivityScope != "all" &&
            session.CanonicalCode != request.ActivityScope)
        {
            return false;
        }

        if (request.TrainingState != "all" &&
            session.ExecutionState != request.TrainingState)
        {
            return false;
        }

        return request.ResultState == "all" ||
               session.ResultState == request.ResultState;
    }

    private static string Message(IReTourEvaluatedSession session)
    {
        if (!session.Visible)
        {
            return "该训练记录暂不可查看。";
        }

        if (session.ResultState == IReTourResultStates.ResultConflicting)
        {
            return "本次训练存在多条不一致的汇总记录。";
        }

        if (session.ResultState == IReTourResultStates.SummaryAvailable)
        {
            return session.ExecutionState == IReTourExecutionStates.Completed
                ? "本次训练有可读取的汇总结果。"
                : "本次训练有汇总记录，但完成状态无法确认。";
        }

        if (session.ResultState == IReTourResultStates.RawOnly)
        {
            return "本次训练只有原始记录，没有可读取的汇总结果。";
        }

        return "本次训练没有可读取的结果。";
    }
}
