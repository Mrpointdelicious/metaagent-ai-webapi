using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using AI_WebApi.IServices.AITools;
using AI_WebApi.Model.AITools;
using AI_WebApi.Model.Domain.IReGo;
using AI_WebApi.Model.DTOs.AITools;
using Microsoft.Extensions.Options;

namespace AI_WebApi.Services.AITools.IReGo;

public sealed class IReGoLongitudinalAnalysisService :
    IIReGoLongitudinalAnalysisService
{
    private const int MaximumReportCount = 10;
    private readonly IIReGoPatientEvaluationService _evaluationService;
    private readonly IIReGoSessionRefProvider _sessionRefProvider;
    private readonly IIReGoEvaluationRuleEngine _evaluationRuleEngine;
    private readonly IReGoToolOptions _options;

    public IReGoLongitudinalAnalysisService(
        IIReGoPatientEvaluationService evaluationService,
        IIReGoSessionRefProvider sessionRefProvider,
        IIReGoEvaluationRuleEngine evaluationRuleEngine,
        IOptions<IReGoToolOptions> options)
    {
        _evaluationService = evaluationService;
        _sessionRefProvider = sessionRefProvider;
        _evaluationRuleEngine = evaluationRuleEngine;
        _options = options.Value;
    }

    public async Task<ToolResponseDto<IReGoLongitudinalAnalysisDto>> GetAsync(
        TrustedPatientContext context,
        GetIReGoLongitudinalAnalysisRequestDto request,
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

        var canonical = evaluation.Sessions
            .Where(x => x.EligibleForLatest)
            .OrderBy(SessionSortTime)
            .ThenBy(x => x.Plan.Id)
            .ToArray();
        if (canonical.Length < 2)
        {
            return Unavailable(
                requestId,
                IReGoReasonCodes.LongitudinalRequiresTwoReports,
                evaluation.Watermark,
                "至少需要两份报告才能进行跨报告比较。");
        }

        var selection = SelectWindow(
            canonical,
            request,
            evaluation.Raw.Patient.BusinessId,
            context.PatientId);
        if (selection.FailureReason != null)
        {
            return selection.Unavailable
                ? Unavailable(
                    requestId,
                    selection.FailureReason,
                    evaluation.Watermark,
                    selection.PatientMessage)
                : Failure(
                    requestId,
                    selection.FailureReason,
                    evaluation.Watermark,
                    selection.PatientMessage);
        }

        var positions = selection.Sessions
            .Select((session, index) => BuildPosition(
                evaluation,
                session,
                index + 1,
                context.PatientId))
            .ToArray();
        var projects = BuildProjects(
            positions,
            request.ProjectScope,
            request.DetailLevel);
        if (projects.Count == 0)
        {
            return Unavailable(
                requestId,
                IReGoReasonCodes.LongitudinalNoComparableProject,
                evaluation.Watermark,
                "所选报告范围内没有符合当前项目范围的训练记录。");
        }

        if (context.Access is { } access)
            foreach (var position in positions)
            {
                var record = "project.dbrehaplan:" + position.Session.Plan.Id;
                access.BindReference(access.Reference("record", record), record);
            }
        var reportDtos = positions.Select(position => ToReportDto(position, context.Access)).ToArray();
        var completeCount = positions.Count(x => x.IsComplete);
        var reasons = new List<string>();
        if (selection.WasTruncated)
        {
            reasons.Add(IReGoReasonCodes.HistoryWindowTruncated);
        }
        if (completeCount != positions.Length)
        {
            reasons.Add(
                IReGoReasonCodes
                    .LongitudinalIncompleteReportsIncludedAsMissing);
        }

        var issues = FilterIssues(
            positions.SelectMany(x => x.Session.Issues).ToArray(),
            request.QualityDetail);
        reasons.AddRange(issues.Select(x => x.Code));
        var trendSeries = projects
            .SelectMany(x => x.Lanes)
            .SelectMany(x => x.MetricSeries)
            .Count(x => x.Interpretation.Mode == "trend");
        var capabilityEvaluationCount = projects
            .SelectMany(x => x.CapabilityEvaluations)
            .Count(x => x.Verdict != "insufficient_data");
        var beforeAfterSeries = projects
            .SelectMany(x => x.Lanes)
            .SelectMany(x => x.MetricSeries)
            .Count(x => x.Interpretation.Mode == "before_after");
        var data = new IReGoLongitudinalAnalysisDto
        {
            PatientRef = _sessionRefProvider.CreatePatient(
                evaluation.Raw.Patient.BusinessId,
                context.PatientId),
            Window = new IReGoLongitudinalWindowDto
            {
                Selector = request.Selector == "session_refs"
                    ? "trusted_session_refs"
                    : request.SelectionMode,
                RequestedReportCount = selection.RequestedCount,
                ResolvedReportCount = positions.Length,
                CompleteReportCount = completeCount,
                IncompleteReportCount = positions.Length - completeCount,
                IsContiguous = true,
                StartedAt = reportDtos
                    .FirstOrDefault(x => x.SessionTime.HasValue)?.SessionTime,
                EndedAt = reportDtos
                    .LastOrDefault(x => x.SessionTime.HasValue)?.SessionTime,
                ComparisonMode = trendSeries > 0
                    ? "trend"
                    : beforeAfterSeries > 0
                        ? "before_after"
                        : completeCount >= 2
                            ? "descriptive_only"
                            : "unavailable"
            },
            Reports = reportDtos,
            Projects = projects,
            CapabilityEvaluations = MergeCapabilityEvaluations(
                projects.SelectMany(x => x.CapabilityEvaluations)),
            AnswerableQuestions = new[]
            {
                "最近几次完整或中断报告分别包含哪些同类型训练项目？",
                "同一训练项目、同一分段的数据库指标在各次报告中如何变化？",
                "某个同名游戏在同难度或跨难度条件下，单位时间得分和游戏任务表现如何变化？",
                "哪些报告或同项目分段缺失，虚线连接跨过了哪些缺失位置？",
                "每份报告的计划项目完成率是多少？"
            },
            Quality = new QualitySummaryDto
            {
                OverallStatus = positions.All(x => x.IsComplete) &&
                    issues.Count == 0
                        ? "valid"
                        : "partial",
                TrendEligibility = trendSeries > 0 ||
                    capabilityEvaluationCount > 0
                    ? "eligible_with_same_project_constraints"
                    : "ineligible",
                LinkStatus = "verified",
                PatientMessage = positions.All(x => x.IsComplete)
                    ? null
                    : "报告序列中存在未完整完成的记录；这些位置只计数并显示为缺失，不参与指标比较。",
                ExplanationBoundary =
                    "只比较同一训练项目的同一分段；不跨项目合并，不填补缺失值，不据此判断临床恢复。",
                Issues = issues
            },
            Sources = new[]
            {
                new IReGoSourceDto
                {
                    SourceSystem = evaluation.Raw.SourceSystem,
                    SourceObject = "project.dbrehaplan",
                    Role = "report_order_plan_and_state",
                    Completeness = evaluation.Raw.SourceCompleteness
                },
                new IReGoSourceDto
                {
                    SourceSystem = evaluation.Raw.SourceSystem,
                    SourceObject = "project.dbreport.ReportDetails.ReportList",
                    Role = "same_project_segment_metrics",
                    Completeness = evaluation.Raw.SourceCompleteness
                }
            }
        };

        AI_WebApi.Services.AITools.PatientContext.RecordTenantPresentation.Annotate(data, context.Access);
        data.SourceSnapshot = AI_WebApi.Services.AITools.PatientContext.RecordTenantPresentation.SourceSnapshot(data, context.Access);

        return ToolResponseDto<IReGoLongitudinalAnalysisDto>.Create(
            "get_irego_longitudinal_analysis",
            requestId,
            reasons.Count == 0 ? ToolStatuses.Success : ToolStatuses.Partial,
            data,
            reasons,
            Meta(evaluation.Watermark));
    }

    private WindowSelection SelectWindow(
        IReadOnlyList<IReGoEvaluatedSession> canonical,
        GetIReGoLongitudinalAnalysisRequestDto request,
        int? tenantId,
        int patientId)
    {
        var selectionMode = request.Selector == "session_refs" &&
                            (request.SessionRefs?.Count ?? 0) > 0
            ? "trusted_session_refs"
            : request.SelectionMode;

        if (selectionMode == "latest_count")
        {
            if (request.ReportCount < 2 ||
                request.ReportCount > MaximumReportCount)
            {
                return WindowSelection.Failed(
                    IReGoReasonCodes.LongitudinalWindowTooLarge,
                    $"一次需选择2至{MaximumReportCount}个连续报告位置。");
            }

            var requested = request.ReportCount;
            var take = Math.Min(requested, canonical.Count);
            return new WindowSelection
            {
                Sessions = canonical.Skip(canonical.Count - take).ToArray(),
                RequestedCount = requested,
                WasTruncated = take < requested
            };
        }

        if (selectionMode == "recent_ordinal_range")
        {
            var startOrdinal = request.StartOrdinal ?? 0;
            var endOrdinal = request.EndOrdinal ?? 0;
            var newerOrdinal = Math.Min(startOrdinal, endOrdinal);
            var olderOrdinal = Math.Max(startOrdinal, endOrdinal);
            if (newerOrdinal < 1 || olderOrdinal > canonical.Count)
            {
                return WindowSelection.Failed(
                    IReGoReasonCodes.InvalidRequest,
                    "所选最近报告序号超出当前患者可用报告范围。");
            }

            var count = olderOrdinal - newerOrdinal + 1;
            if (count < 2 || count > MaximumReportCount)
            {
                return WindowSelection.Failed(
                    IReGoReasonCodes.LongitudinalWindowTooLarge,
                    $"一次需选择2至{MaximumReportCount}个连续报告位置。");
            }

            var firstIndex = canonical.Count - olderOrdinal;
            return new WindowSelection
            {
                Sessions = canonical.Skip(firstIndex).Take(count).ToArray(),
                RequestedCount = count
            };
        }

        if (selectionMode == "date_range")
        {
            var start = request.StartDate;
            var end = request.EndDate;
            if (!start.HasValue || !end.HasValue || start > end)
            {
                return WindowSelection.Failed(
                    IReGoReasonCodes.InvalidRequest,
                    "日期范围无效，请同时提供起止日期且开始日期不晚于结束日期。");
            }

            var chinaOffset = TimeSpan.FromHours(8);
            var localStart = start.Value.ToOffset(chinaOffset).DateTime;
            var localEnd = end.Value.ToOffset(chinaOffset).DateTime;
            var selected = canonical
                .Where(x => SessionSortTime(x) >= localStart &&
                            SessionSortTime(x) <= localEnd)
                .ToArray();
            if (selected.Length < 2)
            {
                return WindowSelection.Failed(
                    IReGoReasonCodes.LongitudinalRequiresTwoReports,
                    "所选日期范围内至少需要两份报告。",
                    unavailable: true);
            }
            if (selected.Length > MaximumReportCount)
            {
                return WindowSelection.Failed(
                    IReGoReasonCodes.LongitudinalWindowTooLarge,
                    $"所选日期范围超过{MaximumReportCount}份报告，请缩小范围。");
            }

            return new WindowSelection
            {
                Sessions = selected,
                RequestedCount = selected.Length
            };
        }

        var supplied = (request.SessionRefs ?? Array.Empty<string>())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (supplied.Length < 2)
        {
            return WindowSelection.Failed(
                IReGoReasonCodes.InvalidRequest,
                "请选择至少两个已由工具返回的会话引用。");
        }

        var indexes = new List<int>();
        foreach (var sessionRef in supplied)
        {
            var index = -1;
            for (var i = 0; i < canonical.Count; i++)
            {
                var session = canonical[i];
                if (_sessionRefProvider.Matches(
                        sessionRef,
                        tenantId,
                        patientId,
                        session.Plan.Id,
                        session.Report.Id,
                        session.ParsedReport.SchemaSignature))
                {
                    index = i;
                    break;
                }
            }

            if (index < 0)
            {
                return WindowSelection.Failed(
                    IReGoReasonCodes.SessionRefNotFound,
                    "存在无法在当前患者上下文中解析的会话引用。");
            }
            indexes.Add(index);
        }

        var first = indexes.Min();
        var last = indexes.Max();
        var resolvedCount = last - first + 1;
        if (resolvedCount > MaximumReportCount)
        {
            return WindowSelection.Failed(
                IReGoReasonCodes.LongitudinalWindowTooLarge,
                $"一次最多比较{MaximumReportCount}个连续报告位置。");
        }

        return new WindowSelection
        {
            Sessions = canonical.Skip(first).Take(resolvedCount).ToArray(),
            RequestedCount = supplied.Length
        };
    }

    private SelectedPosition BuildPosition(
        IReGoPatientEvaluation evaluation,
        IReGoEvaluatedSession session,
        int ordinal,
        int patientId)
    {
        var plan = evaluation.Plans
            .FirstOrDefault(x => x.Plan.Id == session.Plan.Id)
            ?.ParsedPlan ??
            IReGoPlanParser.Parse(session.Plan.Details, session.Template.Details);
        var reportBlocks = session.ParsedReport.DirectTraining
            .Concat(session.ParsedReport.Games)
            .Concat(session.ParsedReport.Assessments)
            .OrderBy(x => x.Sequence)
            .ToArray();
        var isComplete = session.Usable && session.State.State == "completed";
        var sessionRef = _sessionRefProvider.Create(
            evaluation.Raw.Patient!.BusinessId,
            patientId,
            session.Plan.Id,
            session.Report.Id,
            session.ParsedReport.SchemaSignature);
        var completion = isComplete
            ? Completion(plan.Blocks, reportBlocks, ordinal)
            : new CompletionFacts();

        return new SelectedPosition
        {
            Ordinal = ordinal,
            Session = session,
            SessionRef = sessionRef,
            SessionTime = ToChinaOffset(SessionSortTime(session)),
            PlanBlocks = plan.Blocks,
            ReportBlocks = reportBlocks,
            IsComplete = isComplete,
            Completion = completion
        };
    }

    private IReadOnlyList<IReGoLongitudinalProjectDto> BuildProjects(
        IReadOnlyList<SelectedPosition> positions,
        string projectScope,
        string detailLevel)
    {
        var definitions = new Dictionary<string, ProjectDefinition>(
            StringComparer.Ordinal);
        var planByPosition = new Dictionary<(int, string), IReGoContentBlockDto[]>();
        var reportByPosition = new Dictionary<(int, string), IReGoContentBlockDto[]>();

        foreach (var position in positions)
        {
            if (!position.IsComplete)
            {
                continue;
            }

            foreach (var group in position.PlanBlocks.GroupBy(x =>
                         ProjectIdentity(x, position.Ordinal)))
            {
                if (!IsLongitudinalProject(group.First()))
                {
                    continue;
                }
                var definition = Definition(group.Key, group.First());
                if (InScope(definition, projectScope))
                {
                    definitions.TryAdd(group.Key, definition);
                    planByPosition[(position.Ordinal, group.Key)] =
                        group.OrderBy(x => x.Sequence).ToArray();
                }
            }

            foreach (var group in position.ReportBlocks.GroupBy(x =>
                         ProjectIdentity(x, position.Ordinal)))
            {
                if (!IsLongitudinalProject(group.First()))
                {
                    continue;
                }
                var definition = Definition(group.Key, group.First());
                if (InScope(definition, projectScope))
                {
                    reportByPosition[(position.Ordinal, group.Key)] =
                        group.OrderBy(x => x.Sequence).ToArray();
                }
            }
        }

        var result = new List<IReGoLongitudinalProjectDto>();
        foreach (var definition in definitions.Values
                     .OrderBy(x => ProjectOrder(x.RehabContent))
                     .ThenBy(x => x.DisplayName, StringComparer.Ordinal))
        {
            var laneCount = positions.Max(position =>
            {
                planByPosition.TryGetValue(
                    (position.Ordinal, definition.Identity),
                    out var plans);
                return plans?.Length ?? 0;
            });
            if (laneCount == 0)
            {
                continue;
            }

            var alignedPlans = AlignPlanBlocks(
                positions,
                definition.Identity,
                laneCount,
                planByPosition);
            var aligned = AlignReportBlocks(
                positions,
                definition.Identity,
                laneCount,
                alignedPlans,
                reportByPosition);
            var lanes = Enumerable.Range(0, laneCount)
                .Select(index => BuildLane(
                    positions,
                    definition,
                    index,
                    laneCount,
                    aligned,
                    detailLevel))
                .ToArray();
            var capabilityEvaluations = _evaluationRuleEngine
                .AggregateCapabilities(
                    lanes.SelectMany(x => x.MetricSeries)
                        .Select(x => x.Interpretation));
            var modeEvaluation = _evaluationRuleEngine.EvaluateTrainingMode(
                BuildTrainingModeInput(positions, definition));
            var gameEvaluations = Enumerable.Range(0, laneCount)
                .Select(index => _evaluationRuleEngine.EvaluateGamePerformance(
                    BuildGamePerformanceInput(
                        positions,
                        definition,
                        index,
                        aligned)))
                .Where(x => x != null)
                .Select(x => x!)
                .ToArray();
            var combinedEvaluations = capabilityEvaluations
                .Concat(modeEvaluation == null
                    ? Array.Empty<IReGoCapabilityEvaluationDto>()
                    : new[] { modeEvaluation })
                .Concat(gameEvaluations)
                .ToArray();
            result.Add(new IReGoLongitudinalProjectDto
            {
                ProjectKey = ProjectKey(definition.Identity),
                DisplayName = definition.DisplayName,
                RehabContent = definition.RehabContent,
                InteractionType = definition.InteractionType,
                GameMode = definition.GameMode,
                BalanceMode = definition.BalanceMode,
                SameProjectLaneCount = laneCount,
                Lanes = lanes,
                CapabilityEvaluations = combinedEvaluations
            });
        }

        return result;
    }

    private static IReadOnlyDictionary<(int, int), IReGoContentBlockDto>
        AlignPlanBlocks(
            IReadOnlyList<SelectedPosition> positions,
            string identity,
            int laneCount,
            IReadOnlyDictionary<(int, string), IReGoContentBlockDto[]>
                planByPosition)
    {
        var preferredSignatures = new string?[laneCount];
        var result = new Dictionary<(int, int), IReGoContentBlockDto>();
        foreach (var position in positions)
        {
            if (!position.IsComplete ||
                !planByPosition.TryGetValue(
                    (position.Ordinal, identity),
                    out var blocks))
            {
                continue;
            }

            var assignedLanes = new HashSet<int>();
            var assignedBlocks = new HashSet<int>();
            for (var blockIndex = 0; blockIndex < blocks.Length; blockIndex++)
            {
                var signature = ConfigurationSignature(blocks[blockIndex]);
                var lane = -1;
                for (var candidate = 0;
                     candidate < preferredSignatures.Length;
                     candidate++)
                {
                    if (!assignedLanes.Contains(candidate) &&
                        preferredSignatures[candidate] == signature)
                    {
                        lane = candidate;
                        break;
                    }
                }
                if (lane >= 0 && !assignedLanes.Contains(lane))
                {
                    result[(position.Ordinal, lane)] = blocks[blockIndex];
                    assignedLanes.Add(lane);
                    assignedBlocks.Add(blockIndex);
                }
            }

            for (var blockIndex = 0; blockIndex < blocks.Length; blockIndex++)
            {
                if (assignedBlocks.Contains(blockIndex))
                {
                    continue;
                }
                var lane = Enumerable.Range(0, laneCount)
                    .First(x => !assignedLanes.Contains(x));
                result[(position.Ordinal, lane)] = blocks[blockIndex];
                assignedLanes.Add(lane);
                if (preferredSignatures[lane] == null)
                {
                    preferredSignatures[lane] =
                        ConfigurationSignature(blocks[blockIndex]);
                }
            }
        }

        return result;
    }

    private static IReadOnlyDictionary<(int, int), IReGoContentBlockDto>
        AlignReportBlocks(
            IReadOnlyList<SelectedPosition> positions,
            string identity,
            int laneCount,
            IReadOnlyDictionary<(int, int), IReGoContentBlockDto> alignedPlans,
            IReadOnlyDictionary<(int, string), IReGoContentBlockDto[]>
                reportByPosition)
    {
        var result = new Dictionary<(int, int), IReGoContentBlockDto>();
        foreach (var position in positions)
        {
            if (!position.IsComplete ||
                !reportByPosition.TryGetValue(
                    (position.Ordinal, identity),
                    out var reportBlocks))
            {
                continue;
            }

            var planLanes = Enumerable.Range(0, laneCount)
                .Where(lane => alignedPlans.ContainsKey((position.Ordinal, lane)))
                .ToArray();
            var assignedLanes = new HashSet<int>();
            var assignedReports = new HashSet<int>();

            // First bind a report segment to the plan lane only when the
            // normalized configuration signature is identical. Repeated
            // identical configurations are resolved by their original order.
            for (var reportIndex = 0;
                 reportIndex < reportBlocks.Length;
                 reportIndex++)
            {
                var reportSignature =
                    ConfigurationSignature(reportBlocks[reportIndex]);
                var lane = planLanes.FirstOrDefault(
                    candidate =>
                        !assignedLanes.Contains(candidate) &&
                        ConfigurationSignature(
                            alignedPlans[(position.Ordinal, candidate)]) ==
                        reportSignature,
                    -1);
                if (lane < 0)
                {
                    continue;
                }

                result[(position.Ordinal, lane)] = reportBlocks[reportIndex];
                assignedLanes.Add(lane);
                assignedReports.Add(reportIndex);
            }

            // Remaining same-project segments use stable plan/report order.
            // Extra report-only segments are deliberately excluded from
            // longitudinal comparison because no plan lane confirms identity.
            var remainingLanes = planLanes
                .Where(x => !assignedLanes.Contains(x))
                .ToArray();
            var remainingReports = Enumerable.Range(0, reportBlocks.Length)
                .Where(x => !assignedReports.Contains(x))
                .ToArray();
            var fallbackCount = Math.Min(
                remainingLanes.Length,
                remainingReports.Length);
            for (var index = 0; index < fallbackCount; index++)
            {
                result[(position.Ordinal, remainingLanes[index])] =
                    reportBlocks[remainingReports[index]];
            }
        }

        return result;
    }

    private IReGoLongitudinalLaneDto BuildLane(
        IReadOnlyList<SelectedPosition> positions,
        ProjectDefinition definition,
        int laneIndex,
        int laneCount,
        IReadOnlyDictionary<(int, int), IReGoContentBlockDto> aligned,
        string detailLevel)
    {
        var segments = positions.Select(position =>
        {
            if (!position.IsComplete)
            {
                return new IReGoLongitudinalSegmentDto
                {
                    ReportOrdinal = position.Ordinal,
                    Status = "report_incomplete"
                };
            }

            if (!aligned.TryGetValue(
                    (position.Ordinal, laneIndex),
                    out var block))
            {
                return new IReGoLongitudinalSegmentDto
                {
                    ReportOrdinal = position.Ordinal,
                    Status = "segment_missing"
                };
            }

            return new IReGoLongitudinalSegmentDto
            {
                ReportOrdinal = position.Ordinal,
                Status = "present",
                ReportSequence = block.Sequence,
                DisplayName = block.DisplayName,
                ConfigurationSignature = ConfigurationSignature(block),
                GameDifficulty = MetricInt(block, "game_difficulty"),
                Metrics = detailLevel == "standard"
                    ? block.Metrics
                    : Array.Empty<MetricFactDto>()
            };
        }).ToArray();

        var metricCodes = aligned
            .Where(x => x.Key.Item2 == laneIndex)
            .SelectMany(x => x.Value.Metrics)
            .Select(x => x.MetricCode)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();
        var series = metricCodes.Select(code =>
            BuildSeries(code, positions, segments, aligned, laneIndex, definition))
            .ToArray();
        return new IReGoLongitudinalLaneDto
        {
            SameProjectOrdinal = laneIndex + 1,
            DisplayName = laneCount == 1
                ? definition.DisplayName
                : $"{definition.DisplayName}第{laneIndex + 1}段",
            Segments = segments,
            MetricSeries = series
        };
    }

    private IReGoLongitudinalMetricSeriesDto BuildSeries(
        string metricCode,
        IReadOnlyList<SelectedPosition> positions,
        IReadOnlyList<IReGoLongitudinalSegmentDto> segments,
        IReadOnlyDictionary<(int, int), IReGoContentBlockDto> aligned,
        int laneIndex,
        ProjectDefinition definition)
    {
        var metrics = aligned
            .Where(x => x.Key.Item2 == laneIndex)
            .SelectMany(x => x.Value.Metrics)
            .Where(x => x.MetricCode == metricCode)
            .ToArray();
        var unit = metrics.Select(x => x.Unit)
            .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
        var unitStatus = metrics.Select(x => x.UnitStatus)
            .Distinct(StringComparer.Ordinal)
            .Count() == 1
                ? metrics.First().UnitStatus
                : "mixed";
        var observations = positions
            .Select(position =>
            {
                aligned.TryGetValue(
                    (position.Ordinal, laneIndex),
                    out var block);
                return new
                {
                    position.Session.ParsedReport.SchemaFamily,
                    Metric = block?.Metrics.FirstOrDefault(x =>
                        x.MetricCode == metricCode)
                };
            })
            .Where(x =>
                x.Metric?.ValueStatus == "valid" &&
                x.Metric.NormalizedValue.HasValue)
            .ToArray();
        var comparisonLimitations = ComparisonLimitations(
            metricCode,
            observations.Select(x => x.SchemaFamily).ToArray(),
            observations.Select(x => x.Metric!).ToArray());
        var comparisonEligible = comparisonLimitations.Count == 0;
        var schemaFamilies = observations
            .Select(x => x.SchemaFamily)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var points = new List<IReGoLongitudinalMetricPointDto>();
        var previousValidIndex = -1;
        for (var i = 0; i < positions.Count; i++)
        {
            var position = positions[i];
            var segment = segments[i];
            aligned.TryGetValue((position.Ordinal, laneIndex), out var block);
            var metric = block?.Metrics.FirstOrDefault(x =>
                x.MetricCode == metricCode);
            var validNumeric = segment.Status == "present" &&
                metric?.ValueStatus == "valid" &&
                metric.NormalizedValue.HasValue;
            var line = "none";
            var comparisonScope = "none";
            var limitations = new List<string>();
            if (validNumeric && previousValidIndex >= 0 && comparisonEligible)
            {
                var previous = points[previousValidIndex];
                var gap = i - previousValidIndex > 1;
                var crossDifficulty = definition.GameMode.HasValue &&
                    previous.GameDifficulty != segment.GameDifficulty;
                aligned.TryGetValue(
                    (positions[previousValidIndex].Ordinal, laneIndex),
                    out var previousBlock);
                var crossConfiguration = previousBlock != null &&
                    block != null &&
                    ConfigurationSignature(
                        previousBlock,
                        includeGameDifficulty: false) !=
                    ConfigurationSignature(
                        block,
                        includeGameDifficulty: false);
                if (gap)
                {
                    limitations.Add("missing_report_position_skipped");
                }
                if (crossDifficulty)
                {
                    limitations.Add("cross_difficulty");
                }
                if (crossConfiguration)
                {
                    limitations.Add("cross_configuration");
                }
                var conditionLimitations = limitations
                    .Where(x => x != "missing_report_position_skipped")
                    .ToArray();
                comparisonScope = conditionLimitations.Length switch
                {
                    0 => "same_condition",
                    1 => conditionLimitations[0],
                    _ => "multiple_limitations"
                };
                // Missing dates and condition changes remain explicit in the
                // point metadata, while every visible point-to-point segment
                // uses a solid line per the frozen report presentation rule.
                line = "solid";
            }

            var point = new IReGoLongitudinalMetricPointDto
            {
                RecordTenant = position.Session.Plan.RecordTenant,
                OwnerUserRef = position.Session.Plan.OwnerUserRef,
                ReportOrdinal = position.Ordinal,
                SessionRef = position.IsComplete
                    ? position.SessionRef
                    : string.Empty,
                Status = segment.Status != "present"
                    ? segment.Status
                    : metric?.ValueStatus ?? "metric_missing",
                RawValue = metric?.RawValue,
                NormalizedValue = metric?.NormalizedValue,
                DisplayValue = metric == null ? null : DisplayValue(metric),
                GameDifficulty = segment.GameDifficulty,
                ConfigurationSignature = segment.ConfigurationSignature,
                LineFromPrevious = line,
                ComparisonScope = comparisonScope,
                ComparisonLimitations = limitations
            };
            points.Add(point);
            if (validNumeric)
            {
                previousValidIndex = i;
            }
        }

        var longestSameConditionRun = SameConditionRuns(points)
            .Select(x => x.Count)
            .DefaultIfEmpty(0)
            .Max();
        var bestComparableRun = SameConditionRuns(points)
            .OrderByDescending(x => x.Count)
            .ThenByDescending(x => x.Last().ReportOrdinal)
            .FirstOrDefault() ??
            Array.Empty<IReGoLongitudinalMetricPointDto>();
        var conditionBlock = bestComparableRun.Count == 0
            ? null
            : aligned.TryGetValue(
                (bestComparableRun[0].ReportOrdinal, laneIndex),
                out var firstComparableBlock)
                ? firstComparableBlock
                : null;
        var interpretation = _evaluationRuleEngine.EvaluateMetric(
            new IReGoMetricEvaluationInput
            {
                MetricCode = metricCode,
                MetricLabel = IReGoMetricDisplayRegistry.Label(metricCode),
                Unit = unit,
                UnitStatus = unitStatus,
                Values = bestComparableRun
                    .Where(x => x.NormalizedValue.HasValue)
                    .Select(x => x.NormalizedValue!.Value)
                    .ToArray(),
                Conditions = conditionBlock == null
                    ? new Dictionary<string, decimal?>()
                    : ThresholdConditions(conditionBlock)
            });
        return new IReGoLongitudinalMetricSeriesDto
        {
            MetricCode = metricCode,
            DisplayName = IReGoMetricDisplayRegistry.Label(metricCode),
            SchemaFamily = schemaFamilies.Length switch
            {
                0 => "unknown",
                1 => schemaFamilies[0],
                _ => "mixed"
            },
            Unit = unit,
            UnitStatus = unitStatus,
            ComparisonEligible = comparisonEligible,
            ChartEligible = comparisonEligible &&
                IReGoMetricDisplayRegistry.IsChartEligible(metricCode) &&
                points.Count(x => x.Status == "valid" &&
                    x.NormalizedValue.HasValue) > 0,
            LongestSameConditionRun = longestSameConditionRun,
            ComparisonLimitations = comparisonLimitations,
            Points = points,
            Interpretation = comparisonEligible
                ? interpretation
                : Interpret(
                    metricCode,
                    IReGoMetricDisplayRegistry.Label(metricCode),
                    unit,
                    unitStatus,
                    points,
                    definition.GameMode.HasValue,
                    comparisonEligible)
        };
    }

    private static IReadOnlyDictionary<string, decimal?> ThresholdConditions(
        IReGoContentBlockDto block) =>
        new Dictionary<string, decimal?>(StringComparer.Ordinal)
        {
            ["walking_mode"] = MetricValue(block, "walking_mode"),
            ["sit_stand_mode"] = MetricValue(block, "sit_stand_mode"),
            ["balance_mode"] = block.BalanceMode ??
                DecimalInt(MetricValue(block, "balance_mode")),
            ["unloading"] = MetricValue(block, "walking_unloading") ??
                MetricValue(block, "sit_stand_unloading") ??
                MetricValue(block, "balance_unloading"),
            ["force"] = MetricValue(block, "walking_force"),
            ["game_difficulty"] = MetricValue(block, "game_difficulty") ??
                MetricValue(block, "plan_game_intensity")
        };

    private static decimal? MetricValue(
        IReGoContentBlockDto block,
        string metricCode) => block.Metrics.FirstOrDefault(x =>
            x.MetricCode == metricCode &&
            x.ValueStatus == "valid")?.NormalizedValue;

    private static int? DecimalInt(decimal? value) =>
        value.HasValue && value.Value == decimal.Truncate(value.Value)
            ? (int)value.Value
            : null;

    private static IReGoLongitudinalInterpretationDto Interpret(
        string metricCode,
        string label,
        string? unit,
        string unitStatus,
        IReadOnlyList<IReGoLongitudinalMetricPointDto> points,
        bool isGame,
        bool comparisonEligible)
    {
        var valid = points.Where(x =>
                x.Status == "valid" && x.NormalizedValue.HasValue)
            .ToArray();
        if (!comparisonEligible)
        {
            return new IReGoLongitudinalInterpretationDto
            {
                Mode = "unavailable",
                Direction = "insufficient",
                Text = $"{label}仅展示各次记录值，不生成连线或趋势评价。"
            };
        }
        if (valid.Length < 2)
        {
            return new IReGoLongitudinalInterpretationDto
            {
                Text = $"{label}的可用数据点不足，暂不比较。"
            };
        }

        var comparableRuns = SameConditionRuns(points);
        var comparable = comparableRuns
            .OrderByDescending(x => x.Count)
            .ThenByDescending(x => x.Last().ReportOrdinal)
            .FirstOrDefault() ?? Array.Empty<IReGoLongitudinalMetricPointDto>();
        var mode = comparable.Count >= 3
            ? "trend"
            : comparable.Count == 2
                ? "before_after"
                : "descriptive_only";
        var direction = comparable.Count >= 2
            ? Direction(comparable.Select(x => x.NormalizedValue!.Value))
            : "insufficient";
        var suffix = unitStatus == "confirmed" &&
            !string.IsNullOrWhiteSpace(unit)
                ? unit
                : string.Empty;
        var displayed = comparable.Count >= 2 ? comparable : valid;
        var first = $"{displayed.First().NormalizedValue:0.###}{suffix}";
        var last = $"{displayed.Last().NormalizedValue:0.###}{suffix}";
        string text;
        if (mode == "descriptive_only")
        {
            var limitations = points
                .SelectMany(x => x.ComparisonLimitations)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            var notes = new List<string>();
            if (limitations.Contains("missing_report_position_skipped"))
            {
                notes.Add("中间报告日期保留为空位");
            }
            if (limitations.Contains("cross_difficulty"))
            {
                notes.Add("游戏难度发生变化");
            }
            if (limitations.Contains("cross_configuration"))
            {
                notes.Add("训练配置发生变化");
            }
            var conditionText = notes.Count == 0
                ? "没有连续的同条件数据点"
                : string.Join("；", notes);
            text = $"{label}从{first}记录到{last}；{conditionText}，实线只表示可见记录的时间顺序，不代表这些点可以直接评价。";
        }
        else if (isGame &&
            (metricCode is "game_score" or "game_score_rate"))
        {
            var directionText = direction switch
            {
                "increased" => "数值上升",
                "decreased" => "数值下降",
                "unchanged" => "数值持平",
                _ => "数值有升有降"
            };
            text = $"同难度游戏的{label}由{first}记录到{last}，{directionText}；当前评分版本尚未核清，只描述记录，不据此判断运动能力变化。";
        }
        else
        {
            var directionText = direction switch
            {
                "increased" => "数值上升",
                "decreased" => "数值下降",
                "unchanged" => "数值持平",
                _ => "数值有升有降"
            };
            text = mode == "before_after"
                ? $"两次同条件记录中，{label}由{first}变为{last}，{directionText}。"
                : $"同条件完整记录中，{label}从{first}到{last}，整体{directionText}。";
        }

        return new IReGoLongitudinalInterpretationDto
        {
            Mode = mode,
            Direction = direction,
            Text = text
        };
    }

    private static CompletionFacts Completion(
        IReadOnlyList<IReGoContentBlockDto> planBlocks,
        IReadOnlyList<IReGoContentBlockDto> reportBlocks,
        int positionOrdinal)
    {
        var eligiblePlans = planBlocks.Where(IsCompletionEligible).ToArray();
        if (eligiblePlans.Length == 0)
        {
            return new CompletionFacts();
        }

        var reportCounts = reportBlocks
            .GroupBy(x => ProjectIdentity(x, positionOrdinal))
            .ToDictionary(x => x.Key, x => x.Count(), StringComparer.Ordinal);
        var numerator = 0;
        foreach (var planGroup in eligiblePlans.GroupBy(x =>
                     ProjectIdentity(x, positionOrdinal)))
        {
            reportCounts.TryGetValue(planGroup.Key, out var reportCount);
            numerator += Math.Min(planGroup.Count(), reportCount);
        }
        var rate = decimal.Round(
            numerator * 100m / eligiblePlans.Length,
            1,
            MidpointRounding.AwayFromZero);
        return new CompletionFacts
        {
            Rate = rate,
            Numerator = numerator,
            Denominator = eligiblePlans.Length,
            Level = rate switch
            {
                < 70m => "low",
                > 70m => "high",
                _ => "boundary"
            }
        };
    }

    private static bool IsCompletionEligible(IReGoContentBlockDto block) =>
        block.RehabContent is
            "walking" or "sit_stand" or "game" or "vr_game" or
            "balance_training" or "balance_assessment" or
            "single_walking" or "single_sit";

    private static IReGoLongitudinalReportPositionDto ToReportDto(
        SelectedPosition position, PatientAccessContext? access) => new()
        {
            RecordRef = access?.Reference("record", "project.dbrehaplan:" + position.Session.Plan.Id),
            RecordTenant = position.Session.Plan.RecordTenant,
            OwnerUserRef = position.Session.Plan.OwnerUserRef,
            Ordinal = position.Ordinal,
            SessionRef = position.IsComplete
                ? position.SessionRef
                : string.Empty,
            SessionTime = position.SessionTime,
            TrainingState = position.Session.State.State,
            ReportStatus = position.IsComplete ? "complete" : "incomplete",
            IncludedInMetricComparison = position.IsComplete,
            ReportBlockCount = position.IsComplete
                ? position.Session.ParsedReport.ReportBlockCount
                : 0,
            CompletionRate = position.Completion.Rate,
            CompletionNumerator = position.Completion.Numerator,
            CompletionDenominator = position.Completion.Denominator,
            CompletionLevel = position.Completion.Level
        };

    private static IReGoTrainingModeEvaluationInput BuildTrainingModeInput(
        IReadOnlyList<SelectedPosition> positions,
        ProjectDefinition definition)
    {
        var observations = new List<IReGoTrainingModeObservation>();
        foreach (var position in positions)
        {
            var planBlock = position.PlanBlocks.FirstOrDefault(x =>
                ProjectIdentity(x, position.Ordinal) == definition.Identity);
            var reportBlock = position.ReportBlocks.FirstOrDefault(x =>
                ProjectIdentity(x, position.Ordinal) == definition.Identity);
            var block = position.IsComplete
                ? reportBlock ?? planBlock
                : planBlock ?? reportBlock;
            if (block == null)
            {
                continue;
            }
            var isWalking = definition.RehabContent is
                "walking" or "single_walking";
            observations.Add(new IReGoTrainingModeObservation
            {
                ReportOrdinal = position.Ordinal,
                IsComplete = position.IsComplete,
                ModeCode = MetricInt(
                    block,
                    isWalking ? "walking_mode" : "sit_stand_mode") ??
                    MetricInt(
                        block,
                        isWalking ? "plan_walking_mode" : "plan_sit_stand_mode"),
                ModeName = block.TrainingModeName,
                Unloading = MetricValue(
                    block,
                    isWalking
                        ? "walking_unloading"
                        : "sit_stand_unloading") ??
                    MetricValue(
                        block,
                        "plan_weight_loss"),
                Resistance = isWalking
                    ? MetricValue(block, "walking_force") ??
                        MetricValue(block, "plan_resistance")
                    : null
            });
        }
        return new IReGoTrainingModeEvaluationInput
        {
            RehabContent = definition.RehabContent,
            Observations = observations
        };
    }

    private static IReGoGamePerformanceEvaluationInput
        BuildGamePerformanceInput(
            IReadOnlyList<SelectedPosition> positions,
            ProjectDefinition definition,
            int laneIndex,
            IReadOnlyDictionary<(int, int), IReGoContentBlockDto>
                alignedReports)
    {
        if (definition.RehabContent is not ("game" or "vr_game") &&
            definition.InteractionType is not ("game" or "vr_game"))
        {
            return new IReGoGamePerformanceEvaluationInput();
        }

        var observations = new List<IReGoGamePerformanceObservation>();
        foreach (var position in positions)
        {
            var planBlock = position.PlanBlocks
                .Where(x =>
                    ProjectIdentity(x, position.Ordinal) == definition.Identity)
                .OrderBy(x => x.Sequence)
                .ElementAtOrDefault(laneIndex);
            alignedReports.TryGetValue(
                (position.Ordinal, laneIndex),
                out var reportBlock);
            var difficulty = reportBlock == null
                ? null
                : MetricInt(reportBlock, "game_difficulty");
            difficulty ??= planBlock == null
                ? null
                : MetricInt(planBlock, "plan_game_intensity") ??
                  MetricInt(planBlock, "game_difficulty");
            if (!difficulty.HasValue)
            {
                continue;
            }
            observations.Add(new IReGoGamePerformanceObservation
            {
                ReportOrdinal = position.Ordinal,
                IsComplete = position.IsComplete && reportBlock != null,
                Difficulty = difficulty,
                ScoreRate = reportBlock == null
                    ? null
                    : MetricValue(reportBlock, "game_score_rate")
            });
        }
        return new IReGoGamePerformanceEvaluationInput
        {
            DisplayName = definition.DisplayName,
            Observations = observations
        };
    }

    private static IReadOnlyList<IReGoCapabilityEvaluationDto>
        MergeCapabilityEvaluations(
            IEnumerable<IReGoCapabilityEvaluationDto> source)
    {
        var result = new List<IReGoCapabilityEvaluationDto>();
        foreach (var group in source.GroupBy(
                     x => x.CapabilityCode,
                     StringComparer.Ordinal))
        {
            var first = group.First();
            var eligible = group
                .Where(x => x.Verdict != "insufficient_data")
                .ToArray();
            if (eligible.Length == 0)
            {
                result.Add(first);
                continue;
            }
            var verdicts = eligible.Select(x => x.Verdict)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            var verdict = verdicts.Length == 1
                ? verdicts[0]
                : "mixed";
            var message = verdict switch
            {
                "improved" => $"您的{first.CapabilityName}改善。",
                "stable" => $"您的{first.CapabilityName}无明显变化。",
                "worsened" => $"您的{first.CapabilityName}表现下降。",
                _ => $"您的{first.CapabilityName}表现有好有坏。"
            };
            var composer = new IReGoPatientMessageComposer();
            var definition = new IReGoCapabilityDefinition
            {
                Code = first.CapabilityCode,
                Name = first.CapabilityName
            };
            var explanations = eligible
                .Select(x => x.ExtendedExplanation)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.Ordinal)
                .Take(2)
                .ToArray();
            var explanation = verdict == "mixed" && explanations.Length > 0
                ? "相关评价方向不一致。" + string.Join(string.Empty, explanations)
                : string.Join(string.Empty, explanations);
            result.Add(new IReGoCapabilityEvaluationDto
            {
                CapabilityCode = first.CapabilityCode,
                CapabilityName = first.CapabilityName,
                Verdict = verdict,
                EvaluationAxis = eligible.Select(x => x.EvaluationAxis)
                    .Distinct(StringComparer.Ordinal)
                    .Count() == 1
                        ? eligible[0].EvaluationAxis
                        : "multiple",
                Strength = eligible.Any(x =>
                    x.Strength == "external_reference")
                        ? "external_reference"
                        : eligible[0].Strength,
                BasicEvaluation = message,
                ExtendedExplanation = explanation,
                LifeHint = composer.ComposeLifeHint(verdict, definition),
                PatientMessage = message,
                EvidenceMetricCodes = eligible
                    .SelectMany(x => x.EvidenceMetricCodes)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray(),
                MessageRuleVersion = composer.Version,
                RuleSetVersion = first.RuleSetVersion
            });
        }
        return result
            .OrderBy(x => x.CapabilityCode, StringComparer.Ordinal)
            .ToArray();
    }

    private static ProjectDefinition Definition(
        string identity,
        IReGoContentBlockDto block) => new()
        {
            Identity = identity,
            DisplayName = ProjectDisplayName(block),
            RehabContent = block.RehabContent,
            InteractionType = block.InteractionType,
            GameMode = MetricInt(block, "game_mode") ??
                MetricInt(block, "plan_game_mode"),
            BalanceMode = block.BalanceMode ??
                MetricInt(block, "balance_mode") ??
                MetricInt(block, "plan_balance_mode")
        };

    private static string ProjectIdentity(
        IReGoContentBlockDto block,
        int positionOrdinal)
    {
        var content = block.RehabContent.Trim().ToLowerInvariant();
        var interaction = block.InteractionType.Trim().ToLowerInvariant();
        if (content is not (
                "walking" or "single_walking" or
                "sit_stand" or "single_sit" or
                "balance_training" or "balance_assessment" or
                "game" or "vr_game"))
        {
            return $"{content}|{interaction}|unresolved:{positionOrdinal}:{block.Sequence}";
        }

        if (interaction is "game" or "vr_game" ||
            content is "game" or "vr_game")
        {
            var gameInteraction = interaction is "vr_game"
                ? "vr_game"
                : "game";
            var gameMode = MetricInt(block, "game_mode") ??
                MetricInt(block, "plan_game_mode");
            if (gameMode.HasValue)
            {
                return $"game|{gameInteraction}|game:{gameMode.Value}";
            }

            var name = NormalizeDisplayName(block.DisplayName);
            if (!string.IsNullOrWhiteSpace(name) &&
                name is not "训练游戏" and not "计划训练游戏" and not "计划VR游戏")
            {
                return $"game|{gameInteraction}|game_name:{name}";
            }

            // No reliable game identity: isolate this report position so it
            // cannot be compared with an unrelated unknown game.
            return $"game|{gameInteraction}|unknown_game:{positionOrdinal}";
        }

        if (content is "balance_training" or "balance_assessment")
        {
            var balanceMode = block.BalanceMode ??
                MetricInt(block, "balance_mode") ??
                MetricInt(block, "plan_balance_mode");
            return balanceMode.HasValue
                ? $"{content}|{interaction}|balance:{balanceMode.Value.ToString(CultureInfo.InvariantCulture)}"
                : $"{content}|{interaction}|unresolved_balance:{positionOrdinal}:{block.Sequence}";
        }

        return $"{content}|{interaction}";
    }

    private static string NormalizeDisplayName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }
        return Regex.Replace(value.Trim(), "第[0-9]+段$", string.Empty);
    }

    private static string ProjectDisplayName(IReGoContentBlockDto block)
    {
        if (block.InteractionType is "game" or "vr_game")
        {
            var normalized = NormalizeDisplayName(block.DisplayName);
            return string.IsNullOrWhiteSpace(normalized)
                ? "训练游戏"
                : normalized.Replace("计划", string.Empty, StringComparison.Ordinal);
        }

        return block.RehabContent switch
        {
            "walking" => "步行训练",
            "sit_stand" => "坐站训练",
            "balance_training" => "平衡训练",
            "balance_assessment" => "平衡评估",
            "single_walking" => "单侧步行训练",
            "single_sit" => "单侧坐站训练",
            _ => NormalizeDisplayName(block.DisplayName) is { Length: > 0 } name
                ? name.Replace("计划", string.Empty, StringComparison.Ordinal)
                : "训练记录"
        };
    }

    private static string ConfigurationSignature(
        IReGoContentBlockDto block,
        bool includeGameDifficulty = true)
    {
        var configuration = block.Metrics
            .Where(x => x.ValueStatus == "valid")
            .Select(x => new
            {
                Code = ComparableConfigurationCode(x.MetricCode),
                Value = DisplayValue(x)
            })
            .Where(x =>
                x.Code != null &&
                (includeGameDifficulty || x.Code != "difficulty"))
            .GroupBy(x => x.Code!, StringComparer.Ordinal)
            .OrderBy(x => x.Key, StringComparer.Ordinal)
            .Select(x =>
                $"{x.Key}:{string.Join(",", x.Select(v => v.Value).Distinct(StringComparer.Ordinal).OrderBy(v => v, StringComparer.Ordinal))}");
        var content = block.RehabContent is "game" or "vr_game"
            ? "game"
            : block.RehabContent;
        var interaction = block.InteractionType == "vr_game"
            ? "vr_game"
            : block.InteractionType == "game"
                ? "game"
                : block.InteractionType;
        var canonical = string.Join(
            "|",
            new[]
            {
                content,
                interaction,
                block.ReportMode?.ToString(CultureInfo.InvariantCulture) ?? "none",
                block.BalanceMode?.ToString(CultureInfo.InvariantCulture) ?? "none"
            }.Concat(configuration));
        return "cfg_v1_" + Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(canonical)).AsSpan(0, 12))
            .ToLowerInvariant();
    }

    private static IReadOnlyList<string> ComparisonLimitations(
        string metricCode,
        IReadOnlyList<string> schemaFamilies,
        IReadOnlyList<MetricFactDto> metrics)
    {
        var limitations = new List<string>();
        if (schemaFamilies.Distinct(StringComparer.Ordinal).Count() > 1)
        {
            limitations.Add("schema_family_mismatch");
        }

        var unitKeys = metrics
            .Select(x => $"{x.Unit ?? string.Empty}|{x.UnitStatus}")
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var unitEligible = unitKeys.Length == 1 &&
            (metricCode == "game_score"
                ? metrics.All(x =>
                    x.Unit == "score" &&
                    x.UnitStatus is "source_declared" or "confirmed")
                : metrics.All(x =>
                    !string.IsNullOrWhiteSpace(x.Unit) &&
                    x.UnitStatus == "confirmed"));
        if (!unitEligible)
        {
            limitations.Add("unit_not_confirmed_or_mixed");
        }

        return limitations;
    }

    private static IReadOnlyList<IReadOnlyList<IReGoLongitudinalMetricPointDto>>
        SameConditionRuns(
            IReadOnlyList<IReGoLongitudinalMetricPointDto> points)
    {
        var runs = new List<IReadOnlyList<IReGoLongitudinalMetricPointDto>>();
        var current = new List<IReGoLongitudinalMetricPointDto>();
        foreach (var point in points)
        {
            if (point.Status != "valid" || !point.NormalizedValue.HasValue)
            {
                // A report date without this sub-item remains an empty point on
                // the chart, but it does not split the same-condition series.
                continue;
            }

            if (current.Count == 0)
            {
                current.Add(point);
                continue;
            }

            if (point.LineFromPrevious == "solid" &&
                point.ComparisonScope == "same_condition")
            {
                current.Add(point);
                continue;
            }

            runs.Add(current.ToArray());
            current.Clear();
            current.Add(point);
        }

        if (current.Count > 0)
        {
            runs.Add(current.ToArray());
        }
        return runs;
    }

    private static string? ComparableConfigurationCode(string metricCode) =>
        metricCode switch
        {
            "plan_walking_mode" or "walking_mode" => "mode",
            "plan_sit_stand_mode" or "sit_stand_mode" => "mode",
            "plan_set_time" or "walking_set_time" or
                "walking_display_set_time" or "sit_stand_set_time" or
                "game_set_time" or "balance_set_or_record_time" => "set_time",
            "plan_assistance" or "walking_display_assistance" => "assistance",
            "plan_resistance" or "walking_display_resistance" => "resistance",
            "plan_weight_loss" or "walking_unloading" or
                "walking_display_weight" or "sit_stand_unloading" or
                "sit_stand_display_unloading" or "balance_unloading" =>
                "unloading",
            "plan_sit_stand_repetitions" or
                "sit_stand_planned_repetitions" => "planned_repetitions",
            "plan_game_mode" or "game_mode" => "game_mode",
            "plan_game_intensity" or "game_difficulty" => "difficulty",
            "plan_balance_mode" or "balance_mode" => "balance_mode",
            _ => null
        };

    private static string ProjectKey(string identity) =>
        "irego_project_v1_" + Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(identity)).AsSpan(0, 12))
            .ToLowerInvariant();

    private static int? MetricInt(IReGoContentBlockDto block, string code)
    {
        var value = block.Metrics.FirstOrDefault(x =>
            x.MetricCode == code && x.ValueStatus == "valid")?.NormalizedValue;
        return value.HasValue && value.Value == decimal.Truncate(value.Value)
            ? (int)value.Value
            : null;
    }

    private static string DisplayValue(MetricFactDto metric) =>
        metric.RawValue switch
        {
            null => metric.NormalizedValue?.ToString(
                "0.###",
                CultureInfo.InvariantCulture) ?? string.Empty,
            decimal value => value.ToString("0.###", CultureInfo.InvariantCulture),
            double value when double.IsFinite(value) =>
                value.ToString("0.###", CultureInfo.InvariantCulture),
            float value when float.IsFinite(value) =>
                value.ToString("0.###", CultureInfo.InvariantCulture),
            IFormattable value => value.ToString(null, CultureInfo.InvariantCulture),
            _ => metric.RawValue.ToString() ?? string.Empty
        };

    private static string Direction(IEnumerable<decimal> values)
    {
        var materialized = values.ToArray();
        var hasIncrease = false;
        var hasDecrease = false;
        for (var i = 1; i < materialized.Length; i++)
        {
            var delta = materialized[i] - materialized[i - 1];
            if (Math.Abs(delta) <= 0.0001m)
            {
                continue;
            }
            hasIncrease |= delta > 0;
            hasDecrease |= delta < 0;
        }

        return (hasIncrease, hasDecrease) switch
        {
            (true, false) => "increased",
            (false, true) => "decreased",
            (false, false) => "unchanged",
            _ => "variable"
        };
    }

    private static bool InScope(ProjectDefinition definition, string scope) =>
        scope switch
        {
            "walking" => definition.RehabContent is "walking" or "single_walking",
            "sit_stand" => definition.RehabContent is "sit_stand" or "single_sit",
            "games" => definition.InteractionType is "game" or "vr_game",
            "balance" => definition.RehabContent is
                "balance_training" or "balance_assessment",
            _ => true
        };

    private static bool IsLongitudinalProject(IReGoContentBlockDto block)
    {
        if (block.RehabContent is "game" or "vr_game" ||
            block.InteractionType is "game" or "vr_game")
        {
            return MetricInt(block, "game_mode").HasValue ||
                MetricInt(block, "plan_game_mode").HasValue;
        }

        if (block.RehabContent is "balance_training" or "balance_assessment")
        {
            return block.BalanceMode.HasValue ||
                MetricInt(block, "balance_mode").HasValue ||
                MetricInt(block, "plan_balance_mode").HasValue;
        }

        return block.RehabContent is
            "walking" or "single_walking" or
            "sit_stand" or "single_sit";
    }

    private static int ProjectOrder(string content) => content switch
    {
        "walking" => 1,
        "single_walking" => 2,
        "sit_stand" => 3,
        "single_sit" => 4,
        "balance_training" => 5,
        "balance_assessment" => 6,
        "game" => 7,
        "vr_game" => 8,
        _ => 99
    };

    private static IReadOnlyList<ToolIssueDto> FilterIssues(
        IReadOnlyList<ToolIssueDto> issues,
        string detail) => detail switch
        {
            "none" => Array.Empty<ToolIssueDto>(),
            "full" => issues,
            _ => issues.Select(x => new ToolIssueDto
            {
                Code = x.Code,
                Severity = x.Severity,
                Scope = x.Scope,
                Action = x.Action,
                PatientMessage = x.PatientMessage
            }).ToArray()
        };

    private ToolResponseDto<IReGoLongitudinalAnalysisDto> Failure(
        string requestId,
        string reason,
        string? watermark = null,
        string? patientMessage = null) =>
        ToolResponseDto<IReGoLongitudinalAnalysisDto>.Create(
            "get_irego_longitudinal_analysis",
            requestId,
            ToolStatuses.Failed,
            null,
            new[] { reason },
            Meta(watermark),
            patientMessage);

    private ToolResponseDto<IReGoLongitudinalAnalysisDto> Unavailable(
        string requestId,
        string reason,
        string? watermark,
        string? patientMessage) =>
        ToolResponseDto<IReGoLongitudinalAnalysisDto>.Create(
            "get_irego_longitudinal_analysis",
            requestId,
            ToolStatuses.Unavailable,
            null,
            new[] { reason },
            Meta(watermark),
            patientMessage);

    private ToolMetaDto Meta(string? watermark) => new()
    {
        GeneratedAt = DateTimeOffset.UtcNow,
        SourceCompleteness = _options.SourceCompleteness,
        Watermark = watermark
    };

    private static DateTime SessionSortTime(IReGoEvaluatedSession session) =>
        ValidTime(session.Report.UpdateTime) ??
        ValidTime(session.Report.CreateTime) ??
        ValidTime(session.Plan.BookingTime) ??
        ValidTime(session.Plan.CreateTime) ??
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
        var unspecified = DateTime.SpecifyKind(value, DateTimeKind.Unspecified);
        return new DateTimeOffset(unspecified, timeZone.GetUtcOffset(unspecified));
    }

    private sealed class WindowSelection
    {
        public IReadOnlyList<IReGoEvaluatedSession> Sessions { get; init; } =
            Array.Empty<IReGoEvaluatedSession>();
        public int RequestedCount { get; init; }
        public bool WasTruncated { get; init; }
        public string? FailureReason { get; init; }
        public string? PatientMessage { get; init; }
        public bool Unavailable { get; init; }

        public static WindowSelection Failed(
            string reason,
            string patientMessage,
            bool unavailable = false) => new()
            {
                FailureReason = reason,
                PatientMessage = patientMessage,
                Unavailable = unavailable
            };
    }

    private sealed class SelectedPosition
    {
        public int Ordinal { get; init; }
        public IReGoEvaluatedSession Session { get; init; } = new();
        public string SessionRef { get; init; } = string.Empty;
        public DateTimeOffset? SessionTime { get; init; }
        public IReadOnlyList<IReGoContentBlockDto> PlanBlocks { get; init; } =
            Array.Empty<IReGoContentBlockDto>();
        public IReadOnlyList<IReGoContentBlockDto> ReportBlocks { get; init; } =
            Array.Empty<IReGoContentBlockDto>();
        public bool IsComplete { get; init; }
        public CompletionFacts Completion { get; init; } = new();
    }

    private sealed class ProjectDefinition
    {
        public string Identity { get; init; } = string.Empty;
        public string DisplayName { get; init; } = string.Empty;
        public string RehabContent { get; init; } = string.Empty;
        public string InteractionType { get; init; } = string.Empty;
        public int? GameMode { get; init; }
        public int? BalanceMode { get; init; }
    }

    private sealed class CompletionFacts
    {
        public decimal? Rate { get; init; }
        public int Numerator { get; init; }
        public int Denominator { get; init; }
        public string Level { get; init; } = "unavailable";
    }
}
