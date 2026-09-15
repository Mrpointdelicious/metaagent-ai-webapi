using AI_WebApi.IServices.AITools;
using AI_WebApi.Model.AITools;
using AI_WebApi.Model.Domain.IReGo;
using AI_WebApi.Model.DTOs.AITools;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;

namespace AI_WebApi.Services.AITools.PatientContext;

public sealed class MultisourcePatientContextService : IMultisourcePatientContextService
{
    private readonly IIReGoPatientEvaluationService _evaluationService;
    private readonly IIReGoSessionRefProvider _sessionRefProvider;
    private readonly IPatientContextSnapshotCache _cache;
    private readonly IReGoToolOptions _options;

    public MultisourcePatientContextService(
        IIReGoPatientEvaluationService evaluationService,
        IIReGoSessionRefProvider sessionRefProvider,
        IPatientContextSnapshotCache cache,
        IOptions<IReGoToolOptions> options)
    {
        _evaluationService = evaluationService;
        _sessionRefProvider = sessionRefProvider;
        _cache = cache;
        _options = options.Value;
    }

    public async Task<ToolResponseDto<MultisourcePatientContextDto>> GetAsync(
        TrustedPatientContext context,
        GetMultisourcePatientContextRequestDto request,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        if (!_sessionRefProvider.IsConfigured)
        {
            return ToolResponseDto<MultisourcePatientContextDto>.Create(
                "get_multisource_patient_context",
                requestId,
                ToolStatuses.Failed,
                null,
                new[] { IReGoReasonCodes.SessionRefSigningKeyNotConfigured },
                Meta(null));
        }

        if (!request.ForceRefresh &&
            _cache.TryGetSnapshot(context.ContextKey, out var cached))
        {
            return BuildEnvelope(
                requestId,
                WithCacheStatus(cached.Data, "hit"),
                cached.Watermark,
                cached.Data.DataDomains.Irego.Quality.Issues);
        }

        var evaluation = await _evaluationService.EvaluateAsync(
            context,
            cancellationToken);

        if (evaluation.Raw.Patient == null ||
            evaluation.Raw.Patient.IsDeleted == true)
        {
            return ToolResponseDto<MultisourcePatientContextDto>.Create(
                "get_multisource_patient_context",
                requestId,
                ToolStatuses.Failed,
                null,
                new[] { IReGoReasonCodes.PatientNotFound },
                Meta(evaluation.Watermark));
        }

        var now = DateTimeOffset.UtcNow;
        var ttl = TimeSpan.FromSeconds(Math.Clamp(_options.SnapshotTtlSeconds, 30, 3600));
        var expiresAt = now.Add(ttl);
        var usableSessions = evaluation.Sessions
            .Where(x => x.Usable && x.EligibleForLatest)
            .OrderByDescending(SessionSortTime)
            .ToArray();
        var referencedSessions = usableSessions
            .Select(session => new
            {
                SessionRef = _sessionRefProvider.Create(
                    evaluation.Raw.Patient.BusinessId,
                    context.PatientId,
                    session.Plan.Id,
                    session.Report.Id,
                    session.ParsedReport.SchemaSignature),
                Session = session
            })
            .ToArray();
        var bindingDtos = referencedSessions.ToDictionary(
            x => x.SessionRef,
            x => new IReGoSessionBinding
            {
                SessionRef = x.SessionRef,
                ContextKey = context.ContextKey,
                PlanId = x.Session.Plan.Id,
                ReportId = x.Session.Report.Id,
                SchemaSignature = x.Session.ParsedReport.SchemaSignature,
                ExpiresAt = expiresAt
            });
        var latest = referencedSessions.FirstOrDefault();
        var evaluatedPlans = ResolveEvaluatedPlans(evaluation);
        var activePlans = evaluatedPlans
            .Where(x => !x.IsVoided)
            .ToArray();
        var voidedPlans = evaluatedPlans
            .Where(x => x.IsVoided)
            .ToArray();
        var futurePlans = activePlans
            .Where(x => x.State.State == "not_started")
            .OrderBy(x => PlanSortTime(x.Plan))
            .ToArray();
        var unfinishedPlans = activePlans
            .Where(x => x.State.State != "completed")
            .OrderByDescending(x => PlanSortTime(x.Plan))
            .ToArray();
        var recentSessions = referencedSessions
            .Take(10)
            .Select((item, index) => BuildRecentSession(
                item.SessionRef,
                item.Session,
                index + 1))
            .ToArray();
        var effectiveReportCount = activePlans.Count(x =>
            x.EffectiveSession != null);
        var planOnlyCount = activePlans.Count(x =>
            x.EffectiveSession == null);
        var qualityIssues = evaluation.Issues
            .Concat(evaluation.Sessions.SelectMany(x => x.Issues))
            .Concat(new[]
            {
                new ToolIssueDto
                {
                    Code = IReGoReasonCodes.SnapshotCacheNotDistributed,
                    Severity = "warning",
                    Scope = "snapshot",
                    Action = "cache_is_optional_for_session_ref_resolution",
                    Detail =
                        "The snapshot cache is process-local, but session_ref " +
                        "resolution is deterministic and does not depend on it."
                }
            })
            .GroupBy(x => $"{x.Code}|{x.Scope}", StringComparer.Ordinal)
            .Select(x => x.First())
            .ToArray();
        var blockedReasons = ResolveBlockedReasons(usableSessions.Length);
        var patientRef = _sessionRefProvider.CreatePatient(
            evaluation.Raw.Patient.BusinessId,
            context.PatientId);
        var snapshotRef = $"{IReGoDataVersion.SnapshotPrefix}{Guid.NewGuid():N}";

        var data = new MultisourcePatientContextDto
        {
            PatientRef = patientRef,
            IdentityBinding = new IdentityBindingDto
            {
                SubjectRef = context.SubjectRef,
                PatientRef = patientRef,
                TenantRef = evaluation.Raw.Patient.BusinessId.HasValue
                    ? _sessionRefProvider.CreateTenant(
                        evaluation.Raw.Patient.BusinessId.Value)
                    : string.Empty,
                Role = "patient",
                BindingStatus = "verified"
            },
            ProfileBrief = BuildProfile(evaluation.Raw.Patient.UserDetails),
            DataDomains = new PatientDataDomainsDto
            {
                Irego = new IReGoContextDomainDto
                {
                    Availability = ResolveAvailability(
                        activePlans.Length,
                        usableSessions.Length),
                    DataState = evaluation.DataState,
                    Counts = new IReGoAvailabilityDto
                    {
                        StrictPlanCount = evaluation.StrictPlanCount,
                        ReportCount = evaluation.StrictReportCount,
                        UsableSessionCount = usableSessions.Length,
                        ReferenceAnomalyCount = evaluation.ReferenceAnomalyCount
                    },
                    AvailabilityFlags = new IReGoAvailabilityFlagsDto
                    {
                        HasEligiblePlan = activePlans.Length > 0,
                        HasLinkedReport = effectiveReportCount > 0,
                        HasUsableSession = usableSessions.Length > 0,
                        HasFuturePlan = futurePlans.Length > 0,
                        HasUnfinishedPlan = unfinishedPlans.Length > 0,
                        HasVoidedPlan = voidedPlans.Length > 0
                    },
                    Coverage = new IReGoCoverageDto
                    {
                        EligiblePlanCount = activePlans.Length,
                        EffectiveReportCount = effectiveReportCount,
                        UsableSessionCount = usableSessions.Length,
                        PlanOnlyCount = planOnlyCount,
                        FuturePlanCount = futurePlans.Length,
                        UnfinishedPlanCount = unfinishedPlans.Length,
                        ExecutionUnknownCount = activePlans.Count(x =>
                            x.State.State == "execution_unknown"),
                        VoidedPlanCount = voidedPlans.Length
                    },
                    PlanStatusSummary = BuildPlanStatusSummary(
                        activePlans,
                        voidedPlans.Length),
                    FuturePlans = futurePlans
                        .Take(10)
                        .Select(x => BuildPlanItem(
                            context,
                            evaluation,
                            x,
                            request.ProjectionLevel))
                        .ToArray(),
                    UnfinishedPlans = unfinishedPlans
                        .Take(10)
                        .Select(x => BuildPlanItem(
                            context,
                            evaluation,
                            x,
                            request.ProjectionLevel))
                        .ToArray(),
                    VoidedPlans = voidedPlans
                        .OrderByDescending(x => PlanSortTime(x.Plan))
                        .Take(10)
                        .Select(x => BuildPlanItem(
                            context,
                            evaluation,
                            x,
                            request.ProjectionLevel))
                        .ToArray(),
                    RecentSessions = recentSessions,
                    SessionHistory = new IReGoSessionHistoryDto
                    {
                        TotalCount = referencedSessions.Length,
                        ReturnedCount = recentSessions.Length,
                        HasMore = referencedSessions.Length > recentSessions.Length
                    },
                    Latest = latest == null
                        ? null
                        : new LatestIReGoSessionDto
                        {
                            SessionRef = latest.SessionRef,
                            SessionTime = ToChinaOffset(SessionSortTime(latest.Session)),
                            TrainingState = latest.Session.State.State,
                            ContentTypes = latest.Session.ParsedReport.ContentTypes,
                            QualityStatus = OverallQuality(latest.Session.Issues)
                        },
                    QueryCapabilities = new IReGoQueryCapabilitiesDto
                    {
                        LatestUsable = usableSessions.Length > 0,
                        BySessionRef = usableSessions.Length > 0,
                        FullAnalysis = usableSessions.Length > 0,
                        Trend = usableSessions.Length >= 3,
                        BlockedReasonCodes = blockedReasons
                    },
                    QueryCapabilityDetails = BuildCapabilityDetails(
                        usableSessions.Length,
                        referencedSessions.Length > recentSessions.Length),
                    EvaluationCapabilities = BuildEvaluationCapabilities(),
                    PausedMetrics = new[]
                    {
                        new IReGoPausedMetricDto
                        {
                            MetricCode = "sit_stand_average_speed",
                            DisplayName = "坐站平均速度",
                            PatientMessage =
                                "坐站平均速度的正负方向含义尚未确认，当前只保留记录值，不用于评价。"
                        },
                        new IReGoPausedMetricDto
                        {
                            MetricCode = "walking_max_speed",
                            DisplayName = "最大步行速度",
                            PatientMessage =
                                "当前记录不能可靠恢复最大步行速度，因此暂不用于评价。"
                        },
                        new IReGoPausedMetricDto
                        {
                            MetricCode = "walking_step_count",
                            DisplayName = "步数",
                            PatientMessage =
                                "当前记录不能可靠恢复步数，因此暂不用于评价。"
                        }
                    },
                    ThresholdCoverage = new IReGoThresholdCoverageDto
                    {
                        IReGoMdcCount = 0,
                        DatabaseThresholdCount = 4,
                        ExternalThresholdProfileCount = 11,
                        PausedMetricCount = 3,
                        RegistryVersion = "irego-threshold-registry-1.1.0",
                        SelectionPolicy =
                            "内部阈值不可用或与最近外部参考相差超过50%时，采用外部MDC下限。"
                    },
                    HistoryNavigation = new IReGoHistoryNavigationDto(),
                    Quality = new QualitySummaryDto
                    {
                        OverallStatus = OverallQuality(qualityIssues),
                        TrendEligibility = usableSessions.Length >= 3
                            ? "eligible_after_same_condition_filter"
                            : "ineligible",
                        LinkStatus = evaluation.ReferenceAnomalyCount == 0
                            ? "verified"
                            : "partial",
                        PatientMessage = FirstPatientMessage(qualityIssues),
                        ExplanationBoundary = usableSessions.Length >= 3
                            ? "可查询单次记录和满足同条件门槛的纵向评价；不得据此判断脑卒中整体病情变化。"
                            : "可查询单次记录；可比记录不足3条时不生成纵向评价。内部质量代码不得直接展示给患者。",
                        Issues = qualityIssues
                    }
                }
            },
            Snapshot = new ContextSnapshotDto
            {
                SnapshotRef = snapshotRef,
                CacheStatus = "generated",
                CacheBackend = "in_memory",
                ExpiresAt = expiresAt,
                Watermark = evaluation.Watermark
            }
        };

        _cache.Store(new PatientContextSnapshot
        {
            SnapshotRef = snapshotRef,
            ContextKey = context.ContextKey,
            ExpiresAt = expiresAt,
            Watermark = evaluation.Watermark,
            Data = data,
            SessionBindings = bindingDtos
        });

        return BuildEnvelope(requestId, data, evaluation.Watermark, qualityIssues);
    }

    private static IReadOnlyList<IReGoEvaluatedPlan> ResolveEvaluatedPlans(
        IReGoPatientEvaluation evaluation)
    {
        if (evaluation.Plans.Count > 0)
        {
            return evaluation.Plans;
        }

        // Compatibility for callers/tests that still construct only Sessions.
        return evaluation.Sessions.Select(session => new IReGoEvaluatedPlan
        {
            Plan = session.Plan,
            Template = session.Template,
            Device = session.Device,
            State = session.State,
            ParsedPlan = IReGo.IReGoPlanParser.Parse(
                session.Plan.Details,
                session.Template.Details),
            EffectiveSession = session,
            Issues = session.Issues
        }).ToArray();
    }

    private IReGoPlanListItemDto BuildPlanItem(
        TrustedPatientContext context,
        IReGoPatientEvaluation evaluation,
        IReGoEvaluatedPlan evaluatedPlan,
        string projectionLevel)
    {
        var plan = evaluatedPlan.Plan;
        var completionFlag = plan.IsComplete switch
        {
            1 => "complete",
            0 => "not_complete",
            _ => "unknown"
        };
        var state = evaluatedPlan.IsVoided
            ? "voided"
            : evaluatedPlan.State.State;
        var patientMessage = state switch
        {
            "voided" => "该计划已作废，不作为待执行计划。",
            "not_started" => "该计划尚未开始。",
            "not_completed" => "该计划记录显示未完整完成。",
            "interrupted" => "该计划在开始后未完整完成。",
            "execution_unknown" when plan.IsComplete == 0 =>
                "该计划未标记为完整完成，但当前无法可靠确认执行过程。",
            _ => null
        };
        var blocks = projectionLevel == "compact"
            ? evaluatedPlan.ParsedPlan.Blocks.Select(WithoutMetrics).ToArray()
            : evaluatedPlan.ParsedPlan.Blocks;

        return new IReGoPlanListItemDto
        {
            PlanRef = _sessionRefProvider.CreatePlan(
                evaluation.Raw.Patient?.BusinessId,
                context.PatientId,
                plan.Id),
            ScheduledAt = ToChinaOffset(PlanSortTime(plan)),
            DisplayName = string.IsNullOrWhiteSpace(evaluatedPlan.Template.Title)
                ? "IREGO训练计划"
                : evaluatedPlan.Template.Title.Trim(),
            State = state,
            CompletionFlag = completionFlag,
            StateReason = evaluatedPlan.IsVoided
                ? "status_minus_one"
                : plan.IsComplete == 0
                    ? "is_complete_equals_zero"
                    : plan.IsComplete == 1
                        ? "is_complete_equals_one"
                        : "completion_flag_unknown",
            PatientMessage = patientMessage,
            DetailAvailable =
                evaluatedPlan.ParsedPlan.Blocks.Count > 0 ||
                evaluatedPlan.EffectiveSession?.Usable == true,
            PlannedBlocks = blocks
        };
    }

    private static IReGoPlanStatusSummaryDto BuildPlanStatusSummary(
        IReadOnlyList<IReGoEvaluatedPlan> activePlans,
        int voidedPlanCount) => new()
        {
            Completed = activePlans.Count(x => x.State.State == "completed"),
            NotStarted = activePlans.Count(x => x.State.State == "not_started"),
            NotCompleted = activePlans.Count(x =>
                x.State.State == "not_completed"),
            Interrupted = activePlans.Count(x => x.State.State == "interrupted"),
            ExecutionUnknown = activePlans.Count(x =>
                x.State.State == "execution_unknown"),
            Voided = voidedPlanCount
        };

    private static IReGoRecentSessionDto BuildRecentSession(
        string sessionRef,
        IReGoEvaluatedSession session,
        int ordinal)
    {
        var (time, timeType, confidence) = ResolveSessionTime(session);
        var content = session.ParsedReport.ContentTypes
            .Select(ContentDisplayName)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToArray();
        var contentText = content.Length == 0
            ? "训练记录"
            : string.Join("、", content);
        var dateText = time?.ToString("yyyy年M月d日") ?? "时间未确认";

        return new IReGoRecentSessionDto
        {
            Ordinal = ordinal,
            SessionRef = sessionRef,
            SessionTime = time,
            TimeType = timeType,
            TimeConfidence = confidence,
            State = session.State.State,
            ContentTypes = session.ParsedReport.ContentTypes,
            ReportBlockCount = session.ParsedReport.ReportBlockCount,
            DisplaySummary =
                $"{dateText} · {contentText} · {session.ParsedReport.ReportBlockCount}段报告记录",
            DetailAvailable = session.Usable
        };
    }

    private static (
        DateTimeOffset? Time,
        string TimeType,
        string Confidence) ResolveSessionTime(
        IReGoEvaluatedSession session)
    {
        var reportTime = ValidTime(session.Report.CreateTime);
        if (reportTime.HasValue)
        {
            return (
                ToChinaOffset(reportTime.Value),
                "report_recorded",
                "medium");
        }

        if (ValidTime(session.Plan.BookingTime).HasValue)
        {
            return (
                ToChinaOffset(session.Plan.BookingTime!.Value),
                "planned",
                "medium");
        }

        return (
            ToChinaOffset(ValidTime(session.Plan.CreateTime) ?? DateTime.MinValue),
            ValidTime(session.Plan.CreateTime).HasValue ? "created" : "unknown",
            "low");
    }

    private static IReadOnlyList<IReGoQueryCapabilityDto> BuildCapabilityDetails(
        int usableSessionCount,
        bool historyWindowTruncated)
    {
        var hasSession = usableSessionCount > 0;
        return new[]
        {
            new IReGoQueryCapabilityDto
            {
                Capability = "latest_usable",
                Available = hasSession,
                Reason = hasSession ? null : IReGoReasonCodes.NoUsableSession,
                PatientMessage = hasSession
                    ? null
                    : "当前没有可解读的训练记录。"
            },
            new IReGoQueryCapabilityDto
            {
                Capability = "by_session_ref",
                Available = hasSession,
                Reason = hasSession ? null : IReGoReasonCodes.NoUsableSession,
                PatientMessage = hasSession
                    ? null
                    : "当前没有可选择的历史训练记录。"
            },
            new IReGoQueryCapabilityDto
            {
                Capability = "full_analysis",
                Available = hasSession,
                Reason = hasSession ? null : IReGoReasonCodes.NoUsableSession,
                PatientMessage = hasSession
                    ? null
                    : "当前没有可展开解读的训练报告。"
            },
            new IReGoQueryCapabilityDto
            {
                Capability = "history_window",
                Available = !historyWindowTruncated,
                Reason = historyWindowTruncated
                    ? IReGoReasonCodes.HistoryWindowTruncated
                    : null,
                PatientMessage = historyWindowTruncated
                    ? "当前只返回最近一部分训练记录。"
                    : null
            },
            new IReGoQueryCapabilityDto
            {
                Capability = "trend",
                Available = usableSessionCount >= 3,
                Reason = usableSessionCount >= 3
                    ? null
                    : IReGoReasonCodes.LongitudinalEvaluationInsufficientData,
                PatientMessage = usableSessionCount >= 3
                    ? "可对至少3条同项目、同条件的完整记录进行分运动能力评价。"
                    : "至少需要3条同项目、同条件的完整记录才能评价变化。"
            },
            new IReGoQueryCapabilityDto
            {
                Capability = "pageable_history",
                Available = true,
                PatientMessage = "可以按页继续查询既往训练和计划。"
            }
        };
    }

    private static IReadOnlyList<IReGoEvaluationCapabilityDto>
        BuildEvaluationCapabilities() => new[]
        {
            new IReGoEvaluationCapabilityDto
            {
                CapabilityCode = "walking_ability",
                CapabilityName = "步行能力",
                SupportLevel = "external_reference",
                PatientMessage = "可在同条件下根据步行速度评价步行能力变化。"
            },
            new IReGoEvaluationCapabilityDto
            {
                CapabilityCode = "active_walking_participation",
                CapabilityName = "自主步行训练能力",
                SupportLevel = "rule_based",
                PatientMessage = "可结合跟随、助力、阻力和减重变化评价自主参与情况。"
            },
            new IReGoEvaluationCapabilityDto
            {
                CapabilityCode = "sit_stand_ability",
                CapabilityName = "坐站能力",
                SupportLevel = "database_threshold",
                PatientMessage = "可在同模式和同减重条件下评价坐站周期变化。"
            },
            new IReGoEvaluationCapabilityDto
            {
                CapabilityCode = "balance_ability",
                CapabilityName = "平衡能力",
                SupportLevel = "external_reference",
                PatientMessage = "可在同平衡条件下根据身体摆动速度评价平衡表现变化。"
            },
            new IReGoEvaluationCapabilityDto
            {
                CapabilityCode = "game_training_performance",
                CapabilityName = "游戏训练表现",
                SupportLevel = "current_fact",
                PatientMessage = "当前只展示游戏得分和可确认时长，不评价改善或下降。"
            }
        };

    private static IReGoContentBlockDto WithoutMetrics(
        IReGoContentBlockDto source) => new()
        {
            Sequence = source.Sequence,
            ReportMode = source.ReportMode,
            BalanceMode = source.BalanceMode,
            RehabContent = source.RehabContent,
            InteractionType = source.InteractionType,
            Name = source.Name,
            DisplayName = source.DisplayName,
            TrainingModeName = source.TrainingModeName
        };

    private static string? FirstPatientMessage(
        IEnumerable<ToolIssueDto> issues) =>
        issues.Select(x => x.PatientMessage)
            .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));

    private static string ContentDisplayName(string contentType) =>
        contentType switch
        {
            "walking" => "步行训练",
            "sit_stand" => "坐站训练",
            "balance_training" => "平衡训练",
            "balance_assessment" => "平衡评估",
            "single_joint" => "单关节训练",
            "single_walking" => "单侧步行训练",
            "single_sit" => "单侧坐站训练",
            "game" => "训练游戏",
            "vr_game" => "VR训练游戏",
            _ => "训练记录"
        };

    private ToolResponseDto<MultisourcePatientContextDto> BuildEnvelope(
        string requestId,
        MultisourcePatientContextDto data,
        string watermark,
        IReadOnlyList<ToolIssueDto> issues)
    {
        var reasons = issues.Select(x => x.Code).ToList();
        if (!data.DataDomains.Irego.QueryCapabilities.LatestUsable)
        {
            reasons.Add(IReGoReasonCodes.NoUsableSession);
        }

        return ToolResponseDto<MultisourcePatientContextDto>.Create(
            "get_multisource_patient_context",
            requestId,
            reasons.Count == 0 ? ToolStatuses.Success : ToolStatuses.Partial,
            data,
            reasons,
            Meta(watermark));
    }

    private IReadOnlyList<string> ResolveBlockedReasons(int usableSessionCount)
    {
        var reasons = new List<string>();
        if (usableSessionCount == 0)
        {
            reasons.Add(IReGoReasonCodes.NoUsableSession);
        }

        return reasons;
    }

    private static PatientProfileBriefDto BuildProfile(string? userDetails)
    {
        if (string.IsNullOrWhiteSpace(userDetails))
        {
            return new PatientProfileBriefDto();
        }

        try
        {
            var root = JObject.Parse(userDetails);
            var allowedFields = new[]
            {
                "Sex", "Gender", "Age", "Height", "Weight", "Diagnosis"
            };
            var facts = new Dictionary<string, object?>(StringComparer.Ordinal);

            foreach (var field in allowedFields)
            {
                var token = root.GetValue(field, StringComparison.OrdinalIgnoreCase);
                if (token == null || token.Type is JTokenType.Null or JTokenType.Undefined)
                {
                    continue;
                }

                facts[ToSnakeCase(field)] = token.Type switch
                {
                    JTokenType.Integer => token.Value<long>(),
                    JTokenType.Float => token.Value<double>(),
                    JTokenType.Boolean => token.Value<bool>(),
                    _ => token.ToString()
                };
            }

            return new PatientProfileBriefDto
            {
                Available = facts.Count > 0,
                Facts = facts
            };
        }
        catch
        {
            return new PatientProfileBriefDto();
        }
    }

    private static MultisourcePatientContextDto WithCacheStatus(
        MultisourcePatientContextDto source,
        string cacheStatus) => new()
        {
            PatientRef = source.PatientRef,
            IdentityBinding = source.IdentityBinding,
            ProfileBrief = source.ProfileBrief,
            DataDomains = source.DataDomains,
            Snapshot = new ContextSnapshotDto
            {
                SnapshotRef = source.Snapshot.SnapshotRef,
                CacheStatus = cacheStatus,
                CacheBackend = source.Snapshot.CacheBackend,
                ExpiresAt = source.Snapshot.ExpiresAt,
                Watermark = source.Snapshot.Watermark
            }
        };

    private ToolMetaDto Meta(string? watermark) => new()
    {
        GeneratedAt = DateTimeOffset.UtcNow,
        SourceCompleteness = _options.SourceCompleteness,
        Watermark = watermark
    };

    private static string ResolveAvailability(int strictPlans, int usableSessions) =>
        usableSessions > 0 ? "available" : strictPlans > 0 ? "partial" : "unavailable";

    private static string OverallQuality(IEnumerable<ToolIssueDto> issues)
    {
        var materialized = issues.ToArray();
        if (materialized.Any(x => x.Severity == "error"))
        {
            return "invalid";
        }

        return materialized.Length > 0 ? "partial" : "valid";
    }

    private static DateTime SessionSortTime(IReGoEvaluatedSession session) =>
        ValidTime(session.Report.CreateTime) ??
        ValidTime(session.Plan.BookingTime) ??
        ValidTime(session.Plan.CreateTime) ??
        DateTime.MinValue;

    private static DateTime PlanSortTime(
        Model.Entities.RehabPlanEntity plan) =>
        ValidTime(plan.BookingTime) ??
        ValidTime(plan.CreateTime) ??
        DateTime.MinValue;

    private static DateTime? ValidTime(DateTime? value) =>
        value.HasValue && value.Value.Year > 1900
            ? value.Value
            : null;

    private static DateTimeOffset? ToChinaOffset(DateTime value)
    {
        if (value == DateTime.MinValue)
        {
            return null;
        }

        var timeZone = IReGo.IReGoPatientEvaluationService.ChinaTimeZone();
        var unspecified = DateTime.SpecifyKind(value, DateTimeKind.Unspecified);
        return new DateTimeOffset(unspecified, timeZone.GetUtcOffset(unspecified));
    }

    private static string ToSnakeCase(string value) =>
        string.Concat(value.Select((character, index) =>
            char.IsUpper(character) && index > 0
                ? $"_{char.ToLowerInvariant(character)}"
                : char.ToLowerInvariant(character).ToString()));
}
