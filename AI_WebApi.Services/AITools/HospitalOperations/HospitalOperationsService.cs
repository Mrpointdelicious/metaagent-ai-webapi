using System.Globalization;
using AI_WebApi.IServices.AITools;
using AI_WebApi.Model.AITools;
using AI_WebApi.Model.DTOs.AITools;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AI_WebApi.Services.AITools.HospitalOperations;

public sealed class HospitalOperationsService : IHospitalOperationsService
{
    private readonly IHospitalOperationsUpstreamClient _upstream;
    private readonly IHospitalOperationsReportHtmlBuilder _htmlBuilder;
    private readonly IIReGoReportImageRenderer _renderer;
    private readonly IHospitalOperationsArtifactIdentityProvider _identityProvider;
    private readonly IHospitalOperationsArtifactStore _artifacts;
    private readonly HospitalOperationsOptions _options;
    private readonly ILogger<HospitalOperationsService> _logger;

    public HospitalOperationsService(
        IHospitalOperationsUpstreamClient upstream,
        IHospitalOperationsReportHtmlBuilder htmlBuilder,
        IIReGoReportImageRenderer renderer,
        IHospitalOperationsArtifactIdentityProvider identityProvider,
        IHospitalOperationsArtifactStore artifacts,
        IOptions<HospitalOperationsOptions> options,
        ILogger<HospitalOperationsService> logger)
    {
        _upstream = upstream;
        _htmlBuilder = htmlBuilder;
        _renderer = renderer;
        _identityProvider = identityProvider;
        _artifacts = artifacts;
        _options = options.Value;
        _logger = logger;
    }

    public Task<IReadOnlyList<HospitalDirectoryEntryDto>> GetDirectoryAsync(
        CancellationToken cancellationToken = default) =>
        _upstream.GetDirectoryAsync(cancellationToken);

    public async Task<HospitalOperationsToolResponseDto> QueryAsync(
        HospitalOperationsQueryRequestDto request,
        string requestBaseUrl,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        var scope = NormalizeScope(request.Scope);
        var section = NormalizeSection(request.Section);
        var outputMode = NormalizeOutputMode(request.OutputMode);
        var depth = NormalizeDepth(request.AnalysisDepth);
        if (scope == null || section == null || outputMode == null || depth == null ||
            !TryPeriod(request, out var startDate, out var endDate))
        {
            return Failure(
                requestId,
                HospitalOperationsReasonCodes.InvalidRequest,
                "请求参数不符合医院运营查询规则。",
                "invalid_request");
        }

        try
        {
            var directory = await _upstream.GetDirectoryAsync(cancellationToken);
            var resolved = ResolveHospitals(request, scope, directory);
            if (!resolved.IsSuccess)
            {
                return Failure(
                    requestId,
                    resolved.ReasonCode!,
                    resolved.Message!,
                    "invalid_request");
            }

            var resolvedHospitals = resolved.Hospitals
                .OrderBy(x => x.HospitalId)
                .ToArray();
            var snapshots = await Task.WhenAll(resolvedHospitals.Select(
                hospital => BuildSnapshotAsync(
                    hospital,
                    section,
                    startDate,
                    endDate,
                    cancellationToken)));
            var comparison = BuildPairwiseComparison(snapshots, section);
            var result = new HospitalOperationsResultDto
            {
                Scope = scope,
                Section = section,
                AnalysisDepth = depth,
                Period = new HospitalOperationsPeriodDto
                {
                    StartDate = startDate.ToString(
                        "yyyy-MM-dd",
                        CultureInfo.InvariantCulture),
                    EndDate = endDate.ToString(
                        "yyyy-MM-dd",
                        CultureInfo.InvariantCulture)
                },
                Hospitals = snapshots,
                Comparison = comparison,
                Analysis = BuildAnalysis(
                    snapshots,
                    section,
                    depth,
                    comparison),
                DataNotes = new[]
                {
                    "医院目录按接口11返回的 Items 实际条目解析，不使用异常的 Count 字段。",
                    "overview 中的平台医院数和学校数属于平台口径，不作为单院数量。",
                    "问诊区间统计与医院详情快照使用不同时间口径，接口中分别标注。"
                }
            };

            if (outputMode == "analysis_and_report")
            {
                if (!_identityProvider.IsConfigured)
                {
                    return Failure(
                        requestId,
                        HospitalOperationsReasonCodes.ReportArtifactNotConfigured,
                        "医院运营数据已查询到，但持久报表签名密钥尚未配置。",
                        "failed",
                        result);
                }
                try
                {
                    var identity = _identityProvider.Create(result);
                    HospitalOperationsArtifactManifestDto manifest;
                    var reused = _artifacts.TryGet(
                        identity.ArtifactRef,
                        out manifest);
                    if (!reused)
                    {
                        var html = _htmlBuilder.Build(result);
                        var png = await _renderer.RenderPngAsync(
                            html,
                            cancellationToken);
                        var stored = _artifacts.Store(
                            new HospitalOperationsArtifactManifestDto
                            {
                                ArtifactRef = identity.ArtifactRef,
                                QueryFingerprint = identity.QueryFingerprint,
                                ContentFingerprint = identity.ContentFingerprint,
                                ContractVersion = identity.ContractVersion,
                                AnalysisRuleVersion = identity.AnalysisRuleVersion,
                                TemplateVersion = identity.TemplateVersion,
                                RenderProfile = identity.RenderProfile,
                                CreatedAt = DateTimeOffset.UtcNow,
                                StorageMode = _artifacts.StorageMode
                            },
                            html,
                            png);
                        manifest = stored.Manifest;
                        reused = stored.Reused;
                    }
                    var configuredBaseUrl =
                        _options.PublicBaseUrl?.Trim().TrimEnd('/');
                    var baseUrl = string.IsNullOrWhiteSpace(configuredBaseUrl)
                        ? requestBaseUrl.TrimEnd('/')
                        : configuredBaseUrl;
                    var artifactPath =
                        "/api/ai/hospital-operations/report-artifacts/" +
                        Uri.EscapeDataString(manifest.ArtifactRef) +
                        "/content";
                    var htmlUrl = baseUrl + artifactPath + "?format=html";
                    var imageUrl = baseUrl + artifactPath + "?format=png";
                    result.Report = new HospitalOperationsReportArtifactDto
                    {
                        ArtifactRef = manifest.ArtifactRef,
                        StorageMode = manifest.StorageMode,
                        HtmlUrl = htmlUrl,
                        ImageUrl = imageUrl,
                        DisplayMarkdown = $"![医院运营分析报告]({imageUrl})",
                        CreatedAt = manifest.CreatedAt,
                        ExpiresAt = null,
                        Reused = reused,
                        QueryFingerprint = manifest.QueryFingerprint,
                        ContentFingerprint = manifest.ContentFingerprint,
                        AnalysisRuleVersion = manifest.AnalysisRuleVersion,
                        TemplateVersion = manifest.TemplateVersion
                    };
                }
                catch (OperationCanceledException)
                    when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    _logger.LogError(
                        exception,
                        "Hospital report rendering failed. RequestId={RequestId}",
                        requestId);
                    return Failure(
                        requestId,
                        HospitalOperationsReasonCodes.ReportRenderingUnavailable,
                        "医院运营数据已查询到，但报表图片暂时无法生成。",
                        "failed",
                        result);
                }
            }

            return new HospitalOperationsToolResponseDto
            {
                RequestId = requestId,
                Status = "success",
                Data = result,
                AgentSummary = ComposeSummary(result)
            };
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Hospital operations query failed. RequestId={RequestId}",
                requestId);
            return Failure(
                requestId,
                HospitalOperationsReasonCodes.UpstreamUnavailable,
                "医院运营数据接口暂时无法访问，请稍后重试。",
                "failed");
        }
    }

    private async Task<HospitalOperationsSnapshotDto> BuildSnapshotAsync(
        HospitalDirectoryEntryDto hospital,
        string section,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken)
    {
        var id = hospital.HospitalId;
        var needOverview = section is "all" or "overview" or "analysis";
        var needConsultation = section is "all" or "consultation" or "analysis";
        var needRooms = section is "all" or "rooms" or "analysis";
        var needDetails = section is "all" or "consultation" or
            "assessment_training" or "records" or "analysis";

        Task<HospitalOverviewDto?> overviewTask = needOverview
            ? OptionalOverviewAsync(id, cancellationToken)
            : Task.FromResult<HospitalOverviewDto?>(null);
        Task<HospitalConsultationDto?> consultationTask = needConsultation
            ? OptionalConsultationAsync(
                id,
                startDate,
                endDate,
                cancellationToken)
            : Task.FromResult<HospitalConsultationDto?>(null);
        Task<HospitalRoomsDto?> roomsTask = needRooms
            ? OptionalRoomsAsync(id, cancellationToken)
            : Task.FromResult<HospitalRoomsDto?>(null);
        Task<(HospitalAssessmentTrainingDto? AssessmentTraining,
            HospitalRecordsDto? Records)> detailsTask = needDetails
            ? OptionalDetailsAsync(id, cancellationToken)
            : Task.FromResult((
                AssessmentTraining: (HospitalAssessmentTrainingDto?)null,
                Records: (HospitalRecordsDto?)null));

        await Task.WhenAll(
            overviewTask,
            consultationTask,
            roomsTask,
            detailsTask);
        var overview = await overviewTask;
        var consultation = await consultationTask;
        var rooms = await roomsTask;
        var details = await detailsTask;
        return new HospitalOperationsSnapshotDto
        {
            Hospital = hospital,
            Overview = overview,
            Consultation = consultation,
            Rooms = rooms,
            AssessmentTraining = details.AssessmentTraining,
            Records = details.Records,
            DerivedMetrics = Derived(overview, consultation, rooms, details.Records)
        };
    }

    private HospitalResolution ResolveHospitals(
        HospitalOperationsQueryRequestDto request,
        string scope,
        IReadOnlyList<HospitalDirectoryEntryDto> directory)
    {
        if (scope == "platform")
        {
            return HospitalResolution.Success(new HospitalDirectoryEntryDto
            {
                HospitalId = 0,
                HospitalName = "全平台",
                TenantLevel = -1
            });
        }

        var ids = scope == "comparison"
            ? request.HospitalIds.Where(x => x > 0).Distinct().ToArray()
            : request.HospitalId is > 0
                ? new[] { request.HospitalId.Value }
                : Array.Empty<int>();
        if (ids.Length == 0)
        {
            return HospitalResolution.Failure(
                HospitalOperationsReasonCodes.InvalidRequest,
                "请选择医院目录中的 hospital_id。" );
        }
        if (scope == "comparison" && ids.Length != 2)
        {
            return HospitalResolution.Failure(
                HospitalOperationsReasonCodes.ComparisonRequiresExactlyTwoHospitals,
                "医院对比必须由用户明确选择且只能选择两家医院。" );
        }

        var selected = new List<HospitalDirectoryEntryDto>();
        foreach (var id in ids)
        {
            var hospital = directory.FirstOrDefault(x => x.HospitalId == id);
            if (hospital == null)
            {
                return HospitalResolution.Failure(
                    HospitalOperationsReasonCodes.HospitalNotFound,
                    $"医院目录中没有 ID {id}。" );
            }
            selected.Add(hospital);
        }

        if (scope == "institution" &&
            !string.IsNullOrWhiteSpace(request.HospitalName) &&
            !SameHospitalName(selected[0].HospitalName, request.HospitalName))
        {
            return HospitalResolution.Failure(
                HospitalOperationsReasonCodes.HospitalIdentityMismatch,
                "hospital_id 与 hospital_name 不匹配，请按医院目录重新选择。" );
        }
        return HospitalResolution.Success(selected.ToArray());
    }

    private static IReadOnlyList<HospitalOperationsMetricDto> Derived(
        HospitalOverviewDto? overview,
        HospitalConsultationDto? consultation,
        HospitalRoomsDto? rooms,
        HospitalRecordsDto? records)
    {
        var values = new List<HospitalOperationsMetricDto>();
        if (overview is { PatientCount: > 0 })
        {
            AddPercent(
                values,
                "daily_active_rate",
                "患者活跃率",
                overview.DailyActiveCount,
                overview.PatientCount);
            if (overview.DoctorCount > 0)
            {
                AddDecimal(
                    values,
                    "patients_per_doctor",
                    "每名医生对应患者数",
                    (decimal)overview.PatientCount / overview.DoctorCount,
                    "人/名");
            }
        }
        if (consultation is { ConsultationPeople: > 0 })
        {
            AddDecimal(
                values,
                "consultations_per_person",
                "人均问诊次数",
                (decimal)consultation.ConsultationTimes /
                consultation.ConsultationPeople,
                "次/人");
        }
        if (rooms is { TotalRooms: > 0 })
        {
            AddPercent(
                values,
                "training_room_share",
                "训练室占比",
                rooms.TrainingRooms,
                rooms.TotalRooms);
        }
        if (records != null)
        {
            var total = records.RealConsultationCount +
                        records.AiConsultationCount +
                        records.RemoteConsultationCount;
            if (total > 0)
            {
                AddPercent(
                    values,
                    "ai_consultation_share",
                    "AI问诊占比",
                    records.AiConsultationCount,
                    total);
            }
        }
        return values;
    }

    private static HospitalOperationsPairwiseComparisonDto?
        BuildPairwiseComparison(
            IReadOnlyList<HospitalOperationsSnapshotDto> snapshots,
            string section)
    {
        if (snapshots.Count != 2)
        {
            return null;
        }

        var left = snapshots[0];
        var right = snapshots[1];
        var metrics = new List<HospitalOperationsComparisonMetricDto>();
        var includeAll = section is "all" or "analysis";

        if ((includeAll || section == "overview") &&
            left.Overview != null && right.Overview != null)
        {
            var l = left.Overview;
            var r = right.Overview;
            AddComparisonMetric(metrics, left, right, "overview",
                "registered_patients", "注册患者", l.PatientCount,
                r.PatientCount, "人", "current_snapshot");
            AddComparisonMetric(metrics, left, right, "overview",
                "doctors", "医生", l.DoctorCount, r.DoctorCount,
                "人", "current_snapshot");
            AddComparisonMetric(metrics, left, right, "overview",
                "active_patients", "当前活跃患者", l.DailyActiveCount,
                r.DailyActiveCount, "人", "current_snapshot");
            if (l.PatientCount > 0 && r.PatientCount > 0)
            {
                AddComparisonMetric(metrics, left, right, "overview",
                    "daily_active_rate", "患者活跃率",
                    Rate(l.DailyActiveCount, l.PatientCount),
                    Rate(r.DailyActiveCount, r.PatientCount),
                    "%", "current_snapshot");
            }
            if (l.DoctorCount > 0 && r.DoctorCount > 0)
            {
                AddComparisonMetric(metrics, left, right, "overview",
                    "patients_per_doctor", "每名医生对应患者数",
                    (decimal)l.PatientCount / l.DoctorCount,
                    (decimal)r.PatientCount / r.DoctorCount,
                    "人/名", "current_snapshot");
            }
        }

        if ((includeAll || section == "consultation") &&
            left.Consultation != null && right.Consultation != null)
        {
            var l = left.Consultation;
            var r = right.Consultation;
            AddComparisonMetric(metrics, left, right, "consultation",
                "consultation_people", "问诊人数", l.ConsultationPeople,
                r.ConsultationPeople, "人", "query_window");
            AddComparisonMetric(metrics, left, right, "consultation",
                "consultation_times", "问诊次数", l.ConsultationTimes,
                r.ConsultationTimes, "次", "query_window");
            if (l.ConsultationPeople > 0 && r.ConsultationPeople > 0)
            {
                AddComparisonMetric(metrics, left, right, "consultation",
                    "consultations_per_person", "人均问诊次数",
                    (decimal)l.ConsultationTimes / l.ConsultationPeople,
                    (decimal)r.ConsultationTimes / r.ConsultationPeople,
                    "次/人", "query_window");
            }
        }

        if ((includeAll || section == "rooms") &&
            left.Rooms != null && right.Rooms != null)
        {
            var l = left.Rooms;
            var r = right.Rooms;
            AddComparisonMetric(metrics, left, right, "rooms",
                "consultation_rooms", "问诊室", l.ConsultationRooms,
                r.ConsultationRooms, "间", "current_snapshot");
            AddComparisonMetric(metrics, left, right, "rooms",
                "assessment_rooms", "评估室", l.AssessmentRooms,
                r.AssessmentRooms, "间", "current_snapshot");
            AddComparisonMetric(metrics, left, right, "rooms",
                "training_rooms", "训练室", l.TrainingRooms,
                r.TrainingRooms, "间", "current_snapshot");
            AddComparisonMetric(metrics, left, right, "rooms",
                "total_rooms", "虚拟诊室总数", l.TotalRooms,
                r.TotalRooms, "间", "current_snapshot");
            if (l.TotalRooms > 0 && r.TotalRooms > 0)
            {
                AddComparisonMetric(metrics, left, right, "rooms",
                    "training_room_share", "训练室占比",
                    Rate(l.TrainingRooms, l.TotalRooms),
                    Rate(r.TrainingRooms, r.TotalRooms),
                    "%", "current_snapshot");
            }
        }

        if ((includeAll || section == "assessment_training") &&
            left.AssessmentTraining != null &&
            right.AssessmentTraining != null)
        {
            var l = left.AssessmentTraining;
            var r = right.AssessmentTraining;
            AddComparisonMetric(metrics, left, right, "assessment_training",
                "assessment_count", "评估总次数", l.AssessmentCount,
                r.AssessmentCount, "次", "upstream_snapshot");
            AddComparisonMetric(metrics, left, right, "assessment_training",
                "scale_assessment_count", "量表评估", l.ScaleAssessmentCount,
                r.ScaleAssessmentCount, "次", "upstream_snapshot");
            AddComparisonMetric(metrics, left, right, "assessment_training",
                "paradigm_assessment_count", "范式评估",
                l.ParadigmAssessmentCount, r.ParadigmAssessmentCount,
                "次", "upstream_snapshot");
            AddComparisonMetric(metrics, left, right, "assessment_training",
                "offline_training_count", "离线训练",
                l.OfflineTrainingCount, r.OfflineTrainingCount,
                "次", "upstream_snapshot");
            AddComparisonMetric(metrics, left, right, "assessment_training",
                "device_training_count", "设备训练",
                l.DeviceTrainingCount, r.DeviceTrainingCount,
                "次", "upstream_snapshot");
            AddComparisonMetric(metrics, left, right, "assessment_training",
                "corridor_training_count", "通道训练",
                l.CorridorTrainingCount, r.CorridorTrainingCount,
                "次", "upstream_snapshot");
        }

        if ((includeAll || section == "records") &&
            left.Records != null && right.Records != null)
        {
            var l = left.Records;
            var r = right.Records;
            AddComparisonMetric(metrics, left, right, "records",
                "real_consultation_count", "真人问诊",
                l.RealConsultationCount, r.RealConsultationCount,
                "次", "upstream_snapshot");
            AddComparisonMetric(metrics, left, right, "records",
                "ai_consultation_count", "AI问诊",
                l.AiConsultationCount, r.AiConsultationCount,
                "次", "upstream_snapshot");
            AddComparisonMetric(metrics, left, right, "records",
                "remote_consultation_count", "远程会诊",
                l.RemoteConsultationCount, r.RemoteConsultationCount,
                "次", "upstream_snapshot");
            AddComparisonMetric(metrics, left, right, "records",
                "medical_record_count", "就诊记录",
                l.MedicalRecordCount, r.MedicalRecordCount,
                "条", "upstream_snapshot");
            AddComparisonMetric(metrics, left, right, "records",
                "prescription_template_count", "处方模板",
                l.PrescriptionTemplateCount, r.PrescriptionTemplateCount,
                "条", "upstream_snapshot");
            AddComparisonMetric(metrics, left, right, "records",
                "report_count", "报告", l.ReportCount, r.ReportCount,
                "份", "upstream_snapshot");
        }

        return new HospitalOperationsPairwiseComparisonDto
        {
            LeftHospital = left.Hospital,
            RightHospital = right.Hospital,
            Metrics = metrics,
            Notes = new[]
            {
                "差值统一按右侧医院减左侧医院计算。",
                "数值高低只表示本次接口返回的同口径数据差异，不直接代表运营质量优劣。",
                "query_window 使用请求日期区间；current_snapshot 和 upstream_snapshot 为接口当前快照。"
            }
        };
    }

    private static void AddComparisonMetric(
        ICollection<HospitalOperationsComparisonMetricDto> metrics,
        HospitalOperationsSnapshotDto left,
        HospitalOperationsSnapshotDto right,
        string section,
        string code,
        string label,
        decimal leftValue,
        decimal rightValue,
        string unit,
        string timeScope)
    {
        var normalizedLeft = Math.Round(
            leftValue,
            2,
            MidpointRounding.AwayFromZero);
        var normalizedRight = Math.Round(
            rightValue,
            2,
            MidpointRounding.AwayFromZero);
        var difference = Math.Round(
            normalizedRight - normalizedLeft,
            2,
            MidpointRounding.AwayFromZero);
        var differenceUnit = unit == "%" ? "个百分点" : unit;
        metrics.Add(new HospitalOperationsComparisonMetricDto
        {
            Section = section,
            Code = code,
            Label = label,
            LeftValue = normalizedLeft,
            RightValue = normalizedRight,
            LeftDisplayValue = $"{normalizedLeft:0.##}{unit}",
            RightDisplayValue = $"{normalizedRight:0.##}{unit}",
            Difference = difference,
            DifferenceDisplayValue =
                difference.ToString(
                    "+0.##;-0.##;0",
                    CultureInfo.InvariantCulture) + differenceUnit,
            Unit = unit,
            DifferenceUnit = differenceUnit,
            TimeScope = timeScope,
            LargerValueHospitalId = normalizedLeft == normalizedRight
                ? null
                : normalizedLeft > normalizedRight
                    ? left.Hospital.HospitalId
                    : right.Hospital.HospitalId
        });
    }

    private static IReadOnlyList<HospitalOperationsAnalysisItemDto> BuildAnalysis(
        IReadOnlyList<HospitalOperationsSnapshotDto> snapshots,
        string section,
        string depth,
        HospitalOperationsPairwiseComparisonDto? comparison)
    {
        var items = new List<HospitalOperationsAnalysisItemDto>();
        foreach (var snapshot in snapshots)
        {
            var name = snapshot.Hospital.HospitalName;
            var overview = snapshot.Overview;
            if (overview != null)
            {
                var activity = Rate(
                    overview.DailyActiveCount,
                    overview.PatientCount);
                items.Add(Item(
                    "service_overview",
                    $"{name}服务概况",
                    $"注册患者{overview.PatientCount:N0}人、医生{overview.DoctorCount:N0}人，" +
                    $"当前活跃患者{overview.DailyActiveCount:N0}人，患者活跃率约{activity:0.0}%。",
                    snapshot));
            }
            if (snapshot.Consultation != null)
            {
                var consultation = snapshot.Consultation;
                var average = consultation.ConsultationPeople == 0
                    ? 0
                    : (decimal)consultation.ConsultationTimes /
                      consultation.ConsultationPeople;
                items.Add(Item(
                    "consultation_activity",
                    $"{name}问诊情况",
                    $"查询期内共有{consultation.ConsultationPeople:N0}人完成" +
                    $"{consultation.ConsultationTimes:N0}次问诊，人均约{average:0.00}次。",
                    snapshot));
            }
            if (snapshot.Rooms != null)
            {
                var rooms = snapshot.Rooms;
                items.Add(Item(
                    "room_capacity",
                    $"{name}虚拟诊室",
                    $"共有{rooms.TotalRooms:N0}间虚拟诊室，其中问诊室" +
                    $"{rooms.ConsultationRooms:N0}间、评估室{rooms.AssessmentRooms:N0}间、" +
                    $"训练室{rooms.TrainingRooms:N0}间。",
                    snapshot));
            }
            if (depth == "standard" && snapshot.AssessmentTraining != null)
            {
                var value = snapshot.AssessmentTraining;
                items.Add(Item(
                    "assessment_training",
                    $"{name}评估与训练",
                    $"详情快照记录评估{value.AssessmentCount:N0}次、离线训练" +
                    $"{value.OfflineTrainingCount:N0}次；设备训练" +
                    $"{value.DeviceTrainingCount:N0}次、通道训练" +
                    $"{value.CorridorTrainingCount:N0}次。",
                    snapshot));
            }
            if (snapshot.Records != null)
            {
                var value = snapshot.Records;
                var consultationTotal = value.RealConsultationCount +
                                        value.AiConsultationCount +
                                        value.RemoteConsultationCount;
                items.Add(Item(
                    "records",
                    $"{name}业务资料",
                    $"详情快照记录{consultationTotal:N0}次分类问诊、" +
                    $"{value.MedicalRecordCount:N0}条就诊记录、" +
                    $"{value.PrescriptionTemplateCount:N0}条处方模板和" +
                    $"{value.ReportCount:N0}份报告。",
                    snapshot));
            }
        }

        if (comparison != null)
        {
            items.Insert(0, new HospitalOperationsAnalysisItemDto
            {
                Code = "pairwise_comparison_overview",
                Title = "两院对比概览",
                Text = $"已对{comparison.LeftHospital.HospitalName}与" +
                       $"{comparison.RightHospital.HospitalName}建立" +
                       $"{comparison.Metrics.Count:N0}项同口径指标对照。" +
                       "差值按右侧医院减左侧医院计算，数值高低不直接代表运营质量优劣。"
            });
        }
        return section == "analysis" || section == "all"
            ? items
            : items.Where(x => MatchesSection(x.Code, section)).ToArray();
    }

    private static bool MatchesSection(string code, string section) => section switch
    {
        "overview" => code == "service_overview",
        "consultation" => code == "consultation_activity",
        "rooms" => code == "room_capacity",
        "assessment_training" => code == "assessment_training",
        "records" => code == "records",
        _ => true
    };

    private async Task<HospitalOverviewDto?> OptionalOverviewAsync(
        int hospitalId,
        CancellationToken cancellationToken) =>
        await _upstream.GetOverviewAsync(hospitalId, cancellationToken);

    private async Task<HospitalConsultationDto?> OptionalConsultationAsync(
        int hospitalId,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken) =>
        await _upstream.GetConsultationAsync(
            hospitalId,
            startDate,
            endDate,
            cancellationToken);

    private async Task<HospitalRoomsDto?> OptionalRoomsAsync(
        int hospitalId,
        CancellationToken cancellationToken) =>
        await _upstream.GetRoomsAsync(hospitalId, cancellationToken);

    private async Task<(HospitalAssessmentTrainingDto? AssessmentTraining,
        HospitalRecordsDto? Records)> OptionalDetailsAsync(
        int hospitalId,
        CancellationToken cancellationToken)
    {
        var value = await _upstream.GetDetailsAsync(
            hospitalId,
            cancellationToken);
        return (value.AssessmentTraining, value.Records);
    }

    private static string ComposeSummary(HospitalOperationsResultDto result)
    {
        var names = string.Join("、", result.Hospitals.Select(
            x => x.Hospital.HospitalName));
        var reportText = result.Report == null
            ? string.Empty
            : " 已生成可重复访问的报表链接。";
        if (result.Comparison != null)
        {
            return $"已完成{result.Comparison.LeftHospital.HospitalName}与" +
                   $"{result.Comparison.RightHospital.HospitalName}的两两运营对比，" +
                   $"共形成{result.Comparison.Metrics.Count:N0}项同口径指标。" +
                   reportText;
        }
        var first = result.Analysis.FirstOrDefault()?.Text;
        return $"已完成{names}的医院运营查询。" +
               (string.IsNullOrWhiteSpace(first) ? string.Empty : $" {first}") +
               reportText;
    }

    private static HospitalOperationsAnalysisItemDto Item(
        string code,
        string title,
        string text,
        HospitalOperationsSnapshotDto snapshot) => new()
        {
            Code = code,
            Title = title,
            Text = text,
            HospitalId = snapshot.Hospital.HospitalId
        };

    private static void AddPercent(
        ICollection<HospitalOperationsMetricDto> values,
        string code,
        string label,
        decimal numerator,
        decimal denominator)
    {
        var value = denominator == 0 ? 0 : numerator * 100m / denominator;
        AddDecimal(values, code, label, value, "%");
    }

    private static void AddDecimal(
        ICollection<HospitalOperationsMetricDto> values,
        string code,
        string label,
        decimal value,
        string unit)
    {
        var rounded = Math.Round(value, 2, MidpointRounding.AwayFromZero);
        values.Add(new HospitalOperationsMetricDto
        {
            Code = code,
            Label = label,
            Value = rounded,
            Unit = unit,
            DisplayValue = $"{rounded:0.##}{unit}"
        });
    }

    private static decimal Rate(decimal numerator, decimal denominator) =>
        denominator == 0 ? 0 : numerator * 100m / denominator;

    private static bool TryPeriod(
        HospitalOperationsQueryRequestDto request,
        out DateOnly start,
        out DateOnly end)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        start = new DateOnly(today.Year, today.Month, 1);
        end = today;
        if (!string.IsNullOrWhiteSpace(request.StartDate) &&
            !DateOnly.TryParseExact(
                request.StartDate.Trim(),
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out start))
        {
            return false;
        }
        if (!string.IsNullOrWhiteSpace(request.EndDate) &&
            !DateOnly.TryParseExact(
                request.EndDate.Trim(),
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out end))
        {
            return false;
        }
        return end >= start && end.DayNumber - start.DayNumber <= 366;
    }

    private static string? NormalizeScope(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            null or "" or "institution" or "hospital" => "institution",
            "platform" => "platform",
            "comparison" or "compare" => "comparison",
            _ => null
        };

    private static string? NormalizeSection(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            null or "" or "all" => "all",
            "overview" => "overview",
            "consultation" => "consultation",
            "rooms" => "rooms",
            "assessment_training" or "assessment" or "training" =>
                "assessment_training",
            "records" => "records",
            "analysis" => "analysis",
            _ => null
        };

    private static string? NormalizeOutputMode(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            null or "" or "analysis" => "analysis",
            "analysis_and_report" or "report" => "analysis_and_report",
            _ => null
        };

    private static string? NormalizeDepth(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            null or "" or "standard" => "standard",
            "summary" => "summary",
            _ => null
        };

    private static bool SameHospitalName(string canonical, string supplied) =>
        NormalizeName(canonical) == NormalizeName(supplied);

    private static string NormalizeName(string value) =>
        new(value.Where(x => !char.IsWhiteSpace(x) &&
                             x is not '(' and not ')' and not '（' and not '）')
            .ToArray());

    private static HospitalOperationsToolResponseDto Failure(
        string requestId,
        string reasonCode,
        string message,
        string status,
        HospitalOperationsResultDto? data = null) => new()
        {
            RequestId = requestId,
            Status = status,
            Data = data,
            ReasonCodes = new[] { reasonCode },
            AgentSummary = message
        };

    private sealed class HospitalResolution
    {
        public IReadOnlyList<HospitalDirectoryEntryDto> Hospitals { get; init; } =
            Array.Empty<HospitalDirectoryEntryDto>();
        public string? ReasonCode { get; init; }
        public string? Message { get; init; }
        public bool IsSuccess => Hospitals.Count > 0;

        public static HospitalResolution Success(
            params HospitalDirectoryEntryDto[] hospitals) => new()
            {
                Hospitals = hospitals
            };

        public static HospitalResolution Failure(
            string reasonCode,
            string message) => new()
            {
                ReasonCode = reasonCode,
                Message = message
            };
    }
}
