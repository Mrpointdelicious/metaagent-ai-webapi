using System.Globalization;
using AI_WebApi.IServices.AITools;
using AI_WebApi.Model.AITools;
using AI_WebApi.Model.Domain.IReGo;
using AI_WebApi.Model.DTOs.AITools;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;

namespace AI_WebApi.Services.AITools.IReGo;

public sealed class IReGoSessionAnalysisService : IIReGoSessionAnalysisService
{
    private readonly IIReGoPatientEvaluationService _evaluationService;
    private readonly IIReGoSessionRefProvider _sessionRefProvider;
    private readonly IPatientContextSnapshotCache _cache;
    private readonly IIReGoStrokeCapabilityMapper _capabilityMapper;
    private readonly IReGoToolOptions _options;

    public IReGoSessionAnalysisService(
        IIReGoPatientEvaluationService evaluationService,
        IIReGoSessionRefProvider sessionRefProvider,
        IPatientContextSnapshotCache cache,
        IIReGoStrokeCapabilityMapper capabilityMapper,
        IOptions<IReGoToolOptions> options)
    {
        _evaluationService = evaluationService;
        _sessionRefProvider = sessionRefProvider;
        _cache = cache;
        _capabilityMapper = capabilityMapper;
        _options = options.Value;
    }

    public async Task<ToolResponseDto<IReGoSessionAnalysisDto>> GetAsync(
        TrustedPatientContext context,
        GetIReGoSessionAnalysisRequestDto request,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        if (!_sessionRefProvider.IsConfigured)
        {
            return Failed(
                requestId,
                IReGoReasonCodes.SessionRefSigningKeyNotConfigured,
                string.Empty);
        }

        var evaluation = await _evaluationService.EvaluateAsync(
            context,
            cancellationToken);

        if (evaluation.Raw.Patient == null ||
            evaluation.Raw.Patient.IsDeleted == true)
        {
            return Failed(
                requestId,
                IReGoReasonCodes.PatientNotFound,
                evaluation.Watermark);
        }

        IReGoEvaluatedSession? session;
        string sessionRef;
        var usableSessions = evaluation.Sessions
            .Where(x => x.Usable)
            .ToArray();

        if (request.Selector == "session_ref")
        {
            if (string.IsNullOrWhiteSpace(request.SessionRef))
            {
                return Failed(
                    requestId,
                    IReGoReasonCodes.SessionRefNotFound,
                    evaluation.Watermark);
            }

            session = usableSessions.FirstOrDefault(x =>
                _sessionRefProvider.Matches(
                    request.SessionRef,
                    evaluation.Raw.Patient.BusinessId,
                    context.PatientId,
                    x.Plan.Id,
                    x.Report.Id,
                    x.ParsedReport.SchemaSignature));
            sessionRef = request.SessionRef;

            if (session == null)
            {
                return Failed(
                    requestId,
                    IReGoReasonCodes.SessionRefNotFound,
                    evaluation.Watermark);
            }
        }
        else
        {
            session = usableSessions
                .Where(x => x.EligibleForLatest)
                .OrderByDescending(SessionSortTime)
                .FirstOrDefault();
            sessionRef = session == null
                ? string.Empty
                : _sessionRefProvider.Create(
                    evaluation.Raw.Patient.BusinessId,
                    context.PatientId,
                    session.Plan.Id,
                    session.Report.Id,
                    session.ParsedReport.SchemaSignature);
        }

        if (session == null)
        {
            return ToolResponseDto<IReGoSessionAnalysisDto>.Create(
                "get_irego_session_analysis",
                requestId,
                ToolStatuses.Unavailable,
                null,
                new[] { IReGoReasonCodes.NoUsableSession },
                Meta(evaluation.Watermark));
        }

        EnsureSessionBinding(context, evaluation, session, sessionRef);

        var parsedPlan = evaluation.Plans
            .FirstOrDefault(x => x.Plan.Id == session.Plan.Id)
            ?.ParsedPlan ??
            IReGoPlanParser.Parse(
                session.Plan.Details,
                session.Template.Details);
        var reportBlocks = session.ParsedReport.DirectTraining
            .Concat(session.ParsedReport.Games)
            .Concat(session.ParsedReport.Assessments)
            .OrderBy(x => x.Sequence)
            .ToArray();
        var comparison = BuildComparison(
            session.Plan.Details,
            session.Template.Details,
            session.ParsedReport.ContentTypes,
            out var comparisonIssues,
            out var planSchemaFamily);
        if (parsedPlan.SchemaFamily != "unknown")
        {
            planSchemaFamily = parsedPlan.SchemaFamily;
        }

        var blockComparison = BuildBlockComparison(
            parsedPlan.Blocks,
            reportBlocks);
        var validDurationFacts = session.ParsedReport.DirectTraining
            .Concat(session.ParsedReport.Games)
            .Concat(session.ParsedReport.Assessments)
            .SelectMany(x => x.Metrics)
            .Where(x =>
                x.ValueStatus == "valid" &&
                x.NormalizedValue.HasValue &&
                x.MetricCode.Contains("duration", StringComparison.Ordinal) &&
                x.Unit == "s" &&
                x.UnitStatus == "confirmed")
            .ToArray();
        decimal? totalTrainingDuration = validDurationFacts.Length == 0
            ? null
            : validDurationFacts.Sum(x => x.NormalizedValue!.Value);
        var durationIssues = ValidateDurationAgainstPlanWindow(
            session.Plan,
            totalTrainingDuration);
        var allIssues = session.Issues
            .Concat(comparisonIssues)
            .Concat(durationIssues)
            .ToArray();
        var visibleIssues = FilterQualityIssues(allIssues, request.QualityDetail);
        var reportTime = ToChinaOffset(
            ValidTime(session.Report.UpdateTime) ??
            ValidTime(session.Report.CreateTime));
        var endTime = ToChinaOffset(session.Plan.EndTime);
        var planPatientMessage = PlanPatientMessage(
            session.State.State,
            session.Plan.IsComplete);

        var data = new IReGoSessionAnalysisDto
        {
            Session = new IReGoSessionIdentityDto
            {
                SessionRef = sessionRef,
                PatientRef = _sessionRefProvider.CreatePatient(
                    evaluation.Raw.Patient.BusinessId,
                    context.PatientId),
                SourceSystem = evaluation.Raw.SourceSystem
            },
            Time = new IReGoSessionTimeDto
            {
                ScheduledAt = ToChinaOffset(session.Plan.BookingTime),
                SourceRecordCreatedAt = ToChinaOffset(session.Plan.CreateTime),
                ExecutionStartedAt = null,
                ReportRecordedAt = reportTime,
                CompletedAt = null,
                TrainingState = session.State.State,
                StateConfidence = session.State.Confidence
            },
            Schema = new IReGoSessionSchemaDto
            {
                PlanSchemaFamily = planSchemaFamily,
                ReportSchemaFamily = session.ParsedReport.SchemaFamily,
                ReportSchemaSignature = session.ParsedReport.SchemaSignature,
                ValidationStatus = session.ValidationStatus
            },
            Overview = new IReGoSessionOverviewDto
            {
                ReportBlockCount = session.ParsedReport.ReportBlockCount,
                ReportBlockLabel = "报告记录分段数",
                ContentTypes = session.ParsedReport.ContentTypes,
                ValidMetricCount = session.ParsedReport.ValidMetricCount,
                ExcludedMetricCount = session.ParsedReport.ExcludedMetricCount,
                TotalTrainingDuration = totalTrainingDuration,
                DurationUnit = totalTrainingDuration.HasValue ? "s" : null,
                TotalTrainingDurationDisplay = totalTrainingDuration.HasValue
                    ? FormatDuration(totalTrainingDuration.Value)
                    : null
            },
            Plan = new IReGoPlanDetailDto
            {
                PlanRef = _sessionRefProvider.CreatePlan(
                    evaluation.Raw.Patient.BusinessId,
                    context.PatientId,
                    session.Plan.Id),
                State = session.State.State,
                CompletionFlag = CompletionFlag(session.Plan.IsComplete),
                StateReason = CompletionStateReason(session.Plan.IsComplete),
                PatientMessage = planPatientMessage,
                Summary = BuildPlanSummary(
                    session.Template.Title,
                    parsedPlan.Blocks.Count),
                Blocks = ApplyDetailLevel(
                    parsedPlan.Blocks,
                    request.DetailLevel)
            },
            ReportBlocks = ApplyDetailLevel(
                reportBlocks,
                request.DetailLevel),
            BlockComparison = blockComparison,
            ReportEvaluationFacts =
                IReGoSingleSessionReportEvaluation.CreateFacts(
                    parsedPlan.Blocks,
                    blockComparison,
                    reportBlocks),
            CurrentPerformanceFacets = BuildCurrentPerformanceFacets(
                reportBlocks),
            PlanReportComparison = comparison,
            Mapping = new IReGoMappingDto(),
            Quality = new QualitySummaryDto
            {
                OverallStatus = OverallQuality(allIssues),
                TrendEligibility = "ineligible",
                LinkStatus = "verified",
                PatientMessage = visibleIssues
                    .Select(x => x.PatientMessage)
                    .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ??
                    planPatientMessage,
                ExplanationBoundary =
                    "只解释本次工具返回的计划、报告分段和指标；不展示内部质量代码，不据此生成诊断或趋势结论。",
                Issues = visibleIssues
            },
            Sources = new[]
            {
                new IReGoSourceDto
                {
                    SourceSystem = evaluation.Raw.SourceSystem,
                    SourceObject = "project.dbrehaplan",
                    Role = "plan_and_state",
                    Completeness = evaluation.Raw.SourceCompleteness
                },
                new IReGoSourceDto
                {
                    SourceSystem = evaluation.Raw.SourceSystem,
                    SourceObject = "project.dbreport.ReportDetails",
                    Role = "session_report",
                    Completeness = evaluation.Raw.SourceCompleteness
                },
                new IReGoSourceDto
                {
                    SourceSystem = evaluation.Raw.SourceSystem,
                    SourceObject = "project.dbtemplates + dbuser + dbdevice",
                    Role = "classification_and_identity_closure",
                    Completeness = evaluation.Raw.SourceCompleteness
                }
            }
        };

        var reasons = allIssues.Select(x => x.Code).Distinct().ToList();

        AI_WebApi.Services.AITools.PatientContext.RecordTenantPresentation.Annotate(data, context.Access);
        data.SourceSnapshot = AI_WebApi.Services.AITools.PatientContext.RecordTenantPresentation.SourceSnapshot(data, context.Access);

        return ToolResponseDto<IReGoSessionAnalysisDto>.Create(
            "get_irego_session_analysis",
            requestId,
            reasons.Count == 0 ? ToolStatuses.Success : ToolStatuses.Partial,
            data,
            reasons,
            Meta(evaluation.Watermark));
    }

    private IReadOnlyList<IReGoCurrentPerformanceFacetDto>
        BuildCurrentPerformanceFacets(
            IReadOnlyList<IReGoContentBlockDto> reportBlocks) =>
        reportBlocks
            .SelectMany(x => x.Metrics)
            .Where(x =>
                x.ValueStatus == "valid" &&
                x.EvaluationStatus != "paused_semantics_unresolved")
            .Select(x => new
            {
                Metric = x,
                Capability = _capabilityMapper.Resolve(x.MetricCode)
            })
            .Where(x => x.Capability != null)
            .GroupBy(x => x.Capability!.Code, StringComparer.Ordinal)
            .Select(group => new IReGoCurrentPerformanceFacetDto
            {
                CapabilityCode = group.Key,
                CapabilityName = group.First().Capability!.Name,
                MetricCodes = group.Select(x => x.Metric.MetricCode)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray(),
                InterpretationMode = "current_fact_only",
                PatientMessage =
                    $"本次记录包含{group.First().Capability!.Name}相关指标；单次训练只展示当前表现，不判断改善或下降。"
            })
            .OrderBy(x => x.CapabilityCode, StringComparer.Ordinal)
            .ToArray();

    private void EnsureSessionBinding(
        TrustedPatientContext context,
        IReGoPatientEvaluation evaluation,
        IReGoEvaluatedSession session,
        string sessionRef)
    {
        if (_cache.TryResolveSession(context.ContextKey, sessionRef, out _))
        {
            return;
        }

        var expiresAt = DateTimeOffset.UtcNow.AddSeconds(
            Math.Clamp(_options.SnapshotTtlSeconds, 30, 3600));
        var binding = new IReGoSessionBinding
        {
            SessionRef = sessionRef,
            ContextKey = context.ContextKey,
            PlanId = session.Plan.Id,
            ReportId = session.Report.Id,
            SchemaSignature = session.ParsedReport.SchemaSignature,
            ExpiresAt = expiresAt
        };

        _cache.StoreSession(binding);
    }

    private static PlanReportComparisonDto BuildComparison(
        string? planDetails,
        string? templateDetails,
        IReadOnlyList<string> reportedContent,
        out IReadOnlyList<ToolIssueDto> issues,
        out string planSchemaFamily)
    {
        var issueList = new List<ToolIssueDto>();
        var planned = new HashSet<string>(StringComparer.Ordinal);
        var candidate = !string.IsNullOrWhiteSpace(planDetails)
            ? planDetails
            : templateDetails;
        planSchemaFamily = "unknown";

        if (!string.IsNullOrWhiteSpace(candidate))
        {
            try
            {
                var token = JToken.Parse(candidate);
                planSchemaFamily = token.Type switch
                {
                    JTokenType.Array => "irego_plan_array",
                    JTokenType.Object => "irego_plan_object",
                    _ => "unknown"
                };

                var properties = token is JContainer container
                    ? container.Descendants().OfType<JProperty>()
                    : Enumerable.Empty<JProperty>();
                foreach (var property in properties)
                {
                    if (property.Name.Equals("ReportMode", StringComparison.OrdinalIgnoreCase) &&
                        int.TryParse(
                            property.Value.ToString(),
                            NumberStyles.Integer,
                            CultureInfo.InvariantCulture,
                            out var mode))
                    {
                        var content = ContentForReportMode(mode);
                        if (content != "unknown")
                        {
                            planned.Add(content);
                        }
                    }

                    if (property.Name.Equals("templateMode", StringComparison.OrdinalIgnoreCase) &&
                        property.Value.ToString() == "1")
                    {
                        issueList.Add(new ToolIssueDto
                        {
                            Code = IReGoReasonCodes.AmbiguousTemplateMode,
                            Severity = "warning",
                            Scope = "plan_schema",
                            Action = "do_not_infer_content",
                            Detail = "templateMode=1 is not mapped without stronger evidence."
                        });
                    }
                }
            }
            catch
            {
                planSchemaFamily = "invalid";
            }
        }

        var reported = reportedContent.Distinct(StringComparer.Ordinal).ToArray();
        var missing = planned.Except(reported, StringComparer.Ordinal).ToArray();
        var unplanned = reported.Except(planned, StringComparer.Ordinal).ToArray();
        issues = issueList;

        return new PlanReportComparisonDto
        {
            Status = planned.Count == 0
                ? "not_comparable"
                : missing.Length == 0 && unplanned.Length == 0
                    ? "matched"
                    : "partial_match",
            PlannedContent = planned.ToArray(),
            ReportedContent = reported,
            MissingFromReport = missing,
            UnplannedInReport = unplanned
        };
    }

    private static IReadOnlyList<IReGoBlockComparisonDto> BuildBlockComparison(
        IReadOnlyList<IReGoContentBlockDto> plannedBlocks,
        IReadOnlyList<IReGoContentBlockDto> reportBlocks)
    {
        var results = new List<IReGoBlockComparisonDto>();
        var usedReportSequences = new HashSet<int>();

        foreach (var planBlock in plannedBlocks.OrderBy(x => x.Sequence))
        {
            var strongCandidates = reportBlocks
                .Where(x =>
                    !usedReportSequences.Contains(x.Sequence) &&
                    x.RehabContent == planBlock.RehabContent &&
                    x.InteractionType == planBlock.InteractionType)
                .OrderBy(x => x.Sequence)
                .ToArray();
            var contentOnlyCandidates = reportBlocks
                .Where(x =>
                    !usedReportSequences.Contains(x.Sequence) &&
                    x.RehabContent == planBlock.RehabContent)
                .OrderBy(x => x.Sequence)
                .ToArray();

            if (strongCandidates.Length == 0)
            {
                if (contentOnlyCandidates.Length > 0)
                {
                    var conflict = contentOnlyCandidates[0];
                    usedReportSequences.Add(conflict.Sequence);
                    results.Add(new IReGoBlockComparisonDto
                    {
                        PlanSequence = planBlock.Sequence,
                        ReportSequence = conflict.Sequence,
                        PlanDisplayName = planBlock.DisplayName,
                        ReportDisplayName = conflict.DisplayName,
                        Status = "conflicting_match",
                        MatchedSignals = new[] { "rehab_content" },
                        Warnings = new[] { "interaction_type_conflict" }
                    });
                }
                else
                {
                    results.Add(new IReGoBlockComparisonDto
                    {
                        PlanSequence = planBlock.Sequence,
                        PlanDisplayName = planBlock.DisplayName,
                        Status = "planned_without_result",
                        MissingFromReport = planBlock.Metrics
                            .Select(x => x.MetricCode)
                            .ToArray()
                    });
                }

                continue;
            }

            var selected = strongCandidates
                .FirstOrDefault(x =>
                    planBlock.ReportMode.HasValue &&
                    x.ReportMode == planBlock.ReportMode) ??
                strongCandidates[0];
            usedReportSequences.Add(selected.Sequence);
            var warnings = new List<string>();
            if (strongCandidates.Length > 1)
            {
                warnings.Add("multiple_same_type_candidates_resolved_by_order");
            }

            var metricComparisons = BuildMetricComparisons(
                planBlock,
                selected,
                warnings,
                out var missing,
                out var unplanned);
            var signals = new List<string>
            {
                "rehab_content",
                "interaction_type"
            };
            if (planBlock.ReportMode.HasValue &&
                selected.ReportMode == planBlock.ReportMode)
            {
                signals.Add("report_mode");
            }
            signals.Add("same_type_order");

            results.Add(new IReGoBlockComparisonDto
            {
                PlanSequence = planBlock.Sequence,
                ReportSequence = selected.Sequence,
                PlanDisplayName = planBlock.DisplayName,
                ReportDisplayName = selected.DisplayName,
                Status = warnings.Count == 0
                    ? "matched_exact"
                    : "matched_with_warning",
                MatchedSignals = signals,
                MissingFromReport = missing,
                UnplannedInReport = unplanned,
                Warnings = warnings,
                MetricComparisons = metricComparisons
            });
        }

        foreach (var reportBlock in reportBlocks
                     .Where(x => !usedReportSequences.Contains(x.Sequence))
                     .OrderBy(x => x.Sequence))
        {
            results.Add(new IReGoBlockComparisonDto
            {
                ReportSequence = reportBlock.Sequence,
                ReportDisplayName = reportBlock.DisplayName,
                Status = "unplanned_report_block",
                UnplannedInReport = reportBlock.Metrics
                    .Select(x => x.MetricCode)
                    .ToArray()
            });
        }

        return results;
    }

    private static IReadOnlyList<IReGoMetricComparisonDto>
        BuildMetricComparisons(
            IReGoContentBlockDto planBlock,
            IReGoContentBlockDto reportBlock,
            ICollection<string> warnings,
            out IReadOnlyList<string> missing,
            out IReadOnlyList<string> unplanned)
    {
        var result = new List<IReGoMetricComparisonDto>();
        var matchedReportCodes = new HashSet<string>(StringComparer.Ordinal);
        var missingCodes = new List<string>();

        foreach (var plannedMetric in planBlock.Metrics)
        {
            var candidateCodes = ReportMetricCandidates(
                plannedMetric.MetricCode,
                reportBlock.RehabContent);
            var reportedMetric = candidateCodes
                .Select(code => reportBlock.Metrics.FirstOrDefault(x =>
                    x.MetricCode == code))
                .FirstOrDefault(x => x != null);
            if (reportedMetric == null)
            {
                missingCodes.Add(plannedMetric.MetricCode);
                result.Add(new IReGoMetricComparisonDto
                {
                    PlannedMetricCode = plannedMetric.MetricCode,
                    PlannedValue = plannedMetric.NormalizedValue,
                    Unit = plannedMetric.Unit,
                    Comparable = false
                });
                continue;
            }

            matchedReportCodes.Add(reportedMetric.MetricCode);
            var comparable =
                plannedMetric.NormalizedValue.HasValue &&
                reportedMetric.NormalizedValue.HasValue &&
                plannedMetric.ValueStatus == "valid" &&
                reportedMetric.ValueStatus == "valid" &&
                plannedMetric.UnitStatus == "confirmed" &&
                reportedMetric.UnitStatus == "confirmed" &&
                !string.IsNullOrWhiteSpace(plannedMetric.Unit) &&
                plannedMetric.Unit == reportedMetric.Unit;
            if (!comparable &&
                plannedMetric.NormalizedValue.HasValue &&
                reportedMetric.NormalizedValue.HasValue)
            {
                warnings.Add(
                    $"metric_not_comparable:{plannedMetric.MetricCode}");
            }

            var relation = comparable
                ? Math.Abs(
                    plannedMetric.NormalizedValue!.Value -
                    reportedMetric.NormalizedValue!.Value) < 0.0001m
                    ? "equal"
                    : "different"
                : "not_comparable";
            result.Add(new IReGoMetricComparisonDto
            {
                PlannedMetricCode = plannedMetric.MetricCode,
                ReportedMetricCode = reportedMetric.MetricCode,
                PlannedValue = plannedMetric.NormalizedValue,
                ReportedValue = reportedMetric.NormalizedValue,
                Unit = comparable ? plannedMetric.Unit : null,
                Comparable = comparable,
                Relation = relation
            });
        }

        missing = missingCodes;
        unplanned = reportBlock.Metrics
            .Where(x => !matchedReportCodes.Contains(x.MetricCode))
            .Select(x => x.MetricCode)
            .ToArray();
        return result;
    }

    private static IReadOnlyList<string> ReportMetricCandidates(
        string plannedMetricCode,
        string rehabContent) =>
        plannedMetricCode switch
        {
            "plan_walking_speed" => new[]
            {
                "walking_average_speed"
            },
            "plan_assistance" => new[]
            {
                "walking_display_assistance"
            },
            "plan_resistance" => new[]
            {
                "walking_display_resistance"
            },
            "plan_weight_loss" => rehabContent == "sit_stand"
                ? new[] { "sit_stand_display_unloading" }
                : new[]
                {
                    "walking_unloading",
                    "walking_display_weight"
                },
            "plan_sit_hold_duration" => new[]
            {
                "sit_display_hold_time"
            },
            "plan_stand_hold_duration" => new[]
            {
                "stand_display_hold_time"
            },
            "plan_set_time" => rehabContent switch
            {
                "walking" => new[] { "walking_set_time" },
                "sit_stand" => new[] { "sit_stand_set_time" },
                "game" or "vr_game" => new[] { "game_set_time" },
                "balance_training" or "balance_assessment" => new[]
                {
                    "balance_set_or_record_time"
                },
                _ => Array.Empty<string>()
            },
            _ => Array.Empty<string>()
        };

    private static string CompletionFlag(int? isComplete) =>
        isComplete switch
        {
            1 => "complete",
            0 => "not_complete",
            _ => "unknown"
        };

    private static string CompletionStateReason(int? isComplete) =>
        isComplete switch
        {
            1 => "is_complete_equals_one",
            0 => "is_complete_equals_zero",
            _ => "completion_flag_unknown"
        };

    private static string? PlanPatientMessage(
        string state,
        int? isComplete) =>
        state switch
        {
            "not_started" => "这项计划尚未开始。",
            "not_completed" => "这项计划记录显示未完整完成。",
            "interrupted" => "这项训练在开始后未完整完成。",
            "execution_unknown" when isComplete == 0 =>
                "这项计划未标记为完整完成，但当前无法可靠确认执行过程。",
            _ => null
        };

    private static string BuildPlanSummary(
        string? templateTitle,
        int blockCount)
    {
        var title = string.IsNullOrWhiteSpace(templateTitle)
            ? "IREGO训练计划"
            : templateTitle.Trim();
        return blockCount > 0
            ? $"{title}，共{blockCount}个计划分段。"
            : $"{title}，当前没有可展开的计划分段。";
    }

    private static IReadOnlyList<IReGoContentBlockDto> ApplyDetailLevel(
        IReadOnlyList<IReGoContentBlockDto> blocks,
        string detailLevel)
    {
        if (detailLevel is not ("summary" or "brief"))
        {
            return blocks;
        }

        return blocks.Select(x => new IReGoContentBlockDto
        {
            Sequence = x.Sequence,
            ReportMode = x.ReportMode,
            BalanceMode = x.BalanceMode,
            RehabContent = x.RehabContent,
            InteractionType = x.InteractionType,
            Name = x.Name,
            DisplayName = x.DisplayName
        }).ToArray();
    }

    private static IReadOnlyList<ToolIssueDto> FilterQualityIssues(
        IReadOnlyList<ToolIssueDto> issues,
        string qualityDetail) =>
        qualityDetail switch
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

    private static IReadOnlyList<ToolIssueDto> ValidateDurationAgainstPlanWindow(
        Model.Entities.RehabPlanEntity plan,
        decimal? durationSeconds)
    {
        if (!durationSeconds.HasValue ||
            !plan.CreateTime.HasValue ||
            !plan.EndTime.HasValue ||
            plan.EndTime.Value <= plan.CreateTime.Value)
        {
            return Array.Empty<ToolIssueDto>();
        }

        var windowSeconds = (decimal)(
            plan.EndTime.Value - plan.CreateTime.Value).TotalSeconds;
        var tolerance = Math.Max(60m, windowSeconds * 0.1m);
        if (durationSeconds.Value <= windowSeconds + tolerance)
        {
            return Array.Empty<ToolIssueDto>();
        }

        return new[]
        {
            new ToolIssueDto
            {
                Code = IReGoReasonCodes.ReportDurationExceedsPlanWindow,
                Severity = "warning",
                Scope = "time",
                Action = "explain_time_conflict",
                Detail =
                    $"Confirmed report duration {durationSeconds.Value} seconds " +
                    $"exceeds plan create-to-end window {windowSeconds} seconds.",
                PatientMessage =
                    "报告中的训练时长明显超过计划记录的时间范围，时间数据可能不完整或存在记录偏差。"
            }
        };
    }

    private static string FormatDuration(decimal seconds)
    {
        var rounded = decimal.Round(seconds, 0, MidpointRounding.AwayFromZero);
        var totalSeconds = Math.Max(0, (long)rounded);
        var minutes = totalSeconds / 60;
        var remainingSeconds = totalSeconds % 60;
        var raw = seconds.ToString("0.###", CultureInfo.InvariantCulture);
        return minutes == 0
            ? $"{raw}秒"
            : $"{raw}秒（约{minutes}分{remainingSeconds}秒）";
    }

    private ToolResponseDto<IReGoSessionAnalysisDto> Failed(
        string requestId,
        string reason,
        string watermark) =>
        ToolResponseDto<IReGoSessionAnalysisDto>.Create(
            "get_irego_session_analysis",
            requestId,
            ToolStatuses.Failed,
            null,
            new[] { reason },
            Meta(watermark));

    private ToolMetaDto Meta(string? watermark) => new()
    {
        GeneratedAt = DateTimeOffset.UtcNow,
        SourceCompleteness = _options.SourceCompleteness,
        Watermark = watermark
    };

    private static DateTime SessionSortTime(IReGoEvaluatedSession session) =>
        ValidTime(session.Report.CreateTime) ??
        ValidTime(session.Plan.BookingTime) ??
        ValidTime(session.Plan.CreateTime) ??
        DateTime.MinValue;

    private static DateTime? ValidTime(DateTime? value) =>
        value.HasValue && value.Value.Year > 1900
            ? value.Value
            : null;

    private static DateTimeOffset? ToChinaOffset(DateTime? value)
    {
        if (!value.HasValue)
        {
            return null;
        }

        var timeZone = IReGoPatientEvaluationService.ChinaTimeZone();
        var unspecified = DateTime.SpecifyKind(value.Value, DateTimeKind.Unspecified);
        return new DateTimeOffset(unspecified, timeZone.GetUtcOffset(unspecified));
    }

    private static string ContentForReportMode(int mode) =>
        mode switch
        {
            1 => "walking",
            2 => "sit_stand",
            4 => "balance_training",
            5 => "single_walking",
            6 => "single_sit",
            _ => "unknown"
        };

    private static string OverallQuality(IEnumerable<ToolIssueDto> issues)
    {
        var materialized = issues.ToArray();
        if (materialized.Any(x => x.Severity == "error"))
        {
            return "invalid";
        }

        return materialized.Any() ? "partial" : "valid";
    }
}
