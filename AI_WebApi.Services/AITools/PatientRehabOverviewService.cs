using System.Globalization;
using System.Text.Json;
using AI_WebApi.IServices.AITools;
using AI_WebApi.Model.DTOs.AITools;
using AI_WebApi.Model.Entities;
using AI_WebApi.Repository.BASE;
using SqlSugar;

namespace AI_WebApi.Services.AITools;


/// <summary>
/// 基础结构
/// </summary>
public class PatientRehabOverviewService
    : IPatientRehabOverviewService
{
    private readonly IBaseRepository<PatientEntity> _patients;
    private readonly IBaseRepository<RehabPlanEntity> _plans;
    private readonly IBaseRepository<RehabReportEntity> _reports;
    private readonly IBaseRepository<DeviceEntity> _devices;
    private readonly IBaseRepository<LegacyDeviceEntity> _legacyDevices;
    private readonly IBaseRepository<RehabTemplateEntity> _templates;

    public PatientRehabOverviewService(
        IBaseRepository<PatientEntity> patients,
        IBaseRepository<RehabPlanEntity> plans,
        IBaseRepository<RehabReportEntity> reports,
        IBaseRepository<DeviceEntity> devices,
        IBaseRepository<LegacyDeviceEntity> legacyDevices,
        IBaseRepository<RehabTemplateEntity> templates)
    {
        _patients = patients;
        _plans = plans;
        _reports = reports;
        _devices = devices;
        _legacyDevices = legacyDevices;
        _templates = templates;
    }


/// <summary>
/// 实现入口
/// </summary>
/// <param name="patientId"></param>
/// <param name="pastDays"></param>
/// <param name="futureDays"></param>
/// <param name="cancellationToken"></param>
/// <returns></returns>
public async Task<PatientRehabOverviewDto?> GetAsync(
    int patientId,
    int pastDays,
    int futureDays,
    CancellationToken cancellationToken = default)
{
    ValidateArguments(patientId, pastDays, futureDays);
    cancellationToken.ThrowIfCancellationRequested();

    var patient = await _patients.Db.Queryable<PatientEntity>()
        .Where(x => x.Id == patientId)
        .FirstAsync();

    if (patient == null || patient.IsDeleted == true)
    {
        return null;
    }

    var timeZone = GetChinaTimeZone();
    var now = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, timeZone);
    var pastStart = now.AddDays(-pastDays);
    var futureEnd = now.AddDays(futureDays);

    var iReGoDeviceIds = await GetIReGoDeviceIdsAsync();

    cancellationToken.ThrowIfCancellationRequested();

    var reportRows = await QueryReportsAsync(
        patientId,
        pastStart.DateTime,
        now.DateTime,
        iReGoDeviceIds);

    var futurePlans = await QueryFuturePlansAsync(
        patientId,
        now.DateTime,
        futureEnd.DateTime,
        iReGoDeviceIds);

    return BuildOverview(
        patient,
        reportRows,
        futurePlans,
        now,
        pastStart,
        futureEnd,
        pastDays,
        futureDays);
}

/// <summary>
/// 参数校验
/// </summary>
/// <param name="patientId"></param>
/// <param name="pastDays"></param>
/// <param name="futureDays"></param>
/// <exception cref="ArgumentOutOfRangeException"></exception>
private static void ValidateArguments(
    int patientId,
    int pastDays,
    int futureDays)
{
    if (patientId <= 0)
        throw new ArgumentOutOfRangeException(nameof(patientId));

    if (pastDays is < 1 or > 90)
        throw new ArgumentOutOfRangeException(nameof(pastDays));

    if (futureDays is < 1 or > 90)
        throw new ArgumentOutOfRangeException(nameof(futureDays));
}

/// <summary>
/// 数据解析和组装 
/// TODO: 暂时排错，后续需要更新
/// </summary>
/// <param name="patient"></param>
/// <param name="reportRows"></param>
/// <param name="futurePlans"></param>
/// <param name="now"></param>
/// <param name="pastStart"></param>
/// <param name="futureEnd"></param>
/// <param name="pastDays"></param>
/// <param name="futureDays"></param>
/// <returns></returns>
private PatientRehabOverviewDto BuildOverview(
    PatientEntity patient,
    List<ReportRow> reportRows,
    List<PlanRow> futurePlans,
    DateTimeOffset now,
    DateTimeOffset pastStart,
    DateTimeOffset futureEnd,
    int pastDays,
    int futureDays)
{
    var qualityNotes = new List<string>();
    var patientDetails = ParsePatientDetails(patient.UserDetails);

    if (!patientDetails.ParseSucceeded)
    {
        qualityNotes.Add("患者扩展资料为空或无法解析");
    }

    var trainingRecords = new List<TrainingRecordDto>();

    foreach (var row in reportRows)
    {
        // 查询条件已经排除了 CreateTime 为空的报告。
        if (!row.ReportTime.HasValue)
        {
            qualityNotes.Add($"报告 {row.ReportId} 缺少生成时间，已跳过");
            continue;
        }

        var reportDetails = ParseReportDetails(row.ReportDetails);
        var planDetails = ParsePlanArray(
            row.PlanDetails,
            "dbrehaplan.Details");

        var recordNotes = new List<string>();
        recordNotes.AddRange(reportDetails.Notes);

        if (!planDetails.ParseSucceeded)
        {
            recordNotes.Add("未能从训练计划中解析项目名称");
        }

        trainingRecords.Add(new TrainingRecordDto
        {
            ReportId = row.ReportId,
            PlanId = row.PlanId,
            TrainingTime = ToChinaOffset(row.ReportTime.Value),
            DeviceType = "iReGo",
            DurationMinutes = reportDetails.DurationMinutes,
            TaskCount = reportDetails.TaskCount,
            WalkingDistanceMeters =
                reportDetails.WalkingDistanceMeters,
            SitToStandCount = reportDetails.SitToStandCount,
            TaskNames = planDetails.TaskNames,
            DataQualityNotes = recordNotes
        });
    }

    var planDtos = new List<FuturePlanDto>();

    foreach (var row in futurePlans)
    {
        if (!row.Plan.BookingTime.HasValue)
        {
            qualityNotes.Add(
                $"计划 {row.Plan.Id} 缺少预约时间，已跳过");
            continue;
        }

        var parsed = ParsePlanDetails(
            row.Plan.Details,
            row.Template?.Details);

        if (!parsed.ParseSucceeded)
        {
            qualityNotes.Add(
                $"计划 {row.Plan.Id} 的训练详情无法解析");
        }

        planDtos.Add(new FuturePlanDto
        {
            PlanId = row.Plan.Id,
            ScheduledAt =
                ToChinaOffset(row.Plan.BookingTime.Value),
            DeviceType = "iReGo",
            TemplateTitle = row.Template?.Title,
            PlannedDurationMinutes = parsed.PlannedMinutes,
            TaskNames = parsed.TaskNames,
            SourceNote = parsed.ParseSucceeded
                ? $"训练内容来源：{parsed.Source}"
                : "训练内容暂时无法从计划或模板中解析"
        });
    }

    var durationValues = trainingRecords
        .Where(x => x.DurationMinutes.HasValue)
        .Select(x => x.DurationMinutes!.Value)
        .ToList();

    var taskCountValues = trainingRecords
        .Where(x => x.TaskCount.HasValue)
        .Select(x => x.TaskCount!.Value)
        .ToList();

    var failedReportCount = trainingRecords.Count(x =>
        x.DataQualityNotes.Count > 0);

    if (failedReportCount > 0)
    {
        qualityNotes.Add(
            $"{failedReportCount}条训练记录存在字段缺失或解析限制");
    }

    return new PatientRehabOverviewDto
    {
        GeneratedAt = now,
        TimeZone = "Asia/Shanghai",

        Patient = new PatientSummaryDto
        {
            PatientId = patient.Id,
            DisplayName = patient.Name ?? string.Empty,
            Sex = patientDetails.Sex,
            Age = patientDetails.Age,
            HeightCm = patientDetails.HeightCm,
            WeightKg = patientDetails.WeightKg,
            DiagnosisSummary =
                patientDetails.DiagnosisSummary
        },

        PastTraining = new PastTrainingSummaryDto
        {
            WindowStart = pastStart,
            WindowEnd = now,
            HasRecords = trainingRecords.Count > 0,
            RecordCount = trainingRecords.Count,
            TotalDurationMinutes =
                durationValues.Count > 0
                    ? durationValues.Sum()
                    : null,
            TotalTaskCount =
                taskCountValues.Count > 0
                    ? taskCountValues.Sum()
                    : null,
            EmptyMessage =
                trainingRecords.Count == 0
                    ? $"近{pastDays}天内没有训练记录"
                    : null,
            Records = trainingRecords
        },

        FuturePlans = new FuturePlanSummaryDto
        {
            WindowStart = now,
            WindowEnd = futureEnd,
            HasPlans = planDtos.Count > 0,
            PlanCount = planDtos.Count,
            EmptyMessage =
                planDtos.Count == 0
                    ? $"未来{futureDays}天内没有训练计划"
                    : null,
            Plans = planDtos
        },

        DataQuality = new DataQualityDto
        {
            Level = GetDataQualityLevel(
                qualityNotes,
                reportRows.Count,
                trainingRecords.Count),
            Notes = qualityNotes.Distinct().ToList()
        }
    };
}

/// <summary>
/// 数据质量评级
/// 查询到报告但时间缺失：跳过并记录质量问题。
/// 报告 JSON 无法解析：仍返回训练记录，指标为 null。
/// 计划详情无法解析：仍返回计划基本信息。
/// 所有可确认的时长和任务数才参与汇总。
/// 没有训练或计划：返回结构化空状态。
/// 没有执行任何数据库写入。
/// </summary>
/// <param name="notes"></param>
/// <param name="sourceReportCount"></param>
/// <param name="returnedRecordCount"></param>
/// <returns></returns>
private static string GetDataQualityLevel(
    List<string> notes,
    int sourceReportCount,
    int returnedRecordCount)
{
    if (sourceReportCount > 0 && returnedRecordCount == 0)
    {
        return "low";
    }

    if (notes.Count > 0)
    {
        return "medium";
    }

    return "high";
}


/// <summary>
/// 获取IReGO设备ID
/// </summary>
/// <returns></returns>
private async Task<List<int>> GetIReGoDeviceIdsAsync()
{
    var currentDevices = await _devices.Db
        .Queryable<DeviceEntity>()
        .Select(x => new { x.Id, x.DeviceType })
        .ToListAsync();

    var legacyDevices = await _legacyDevices.Db
        .Queryable<LegacyDeviceEntity>()
        .Where(x => x.DeviceType == 1)
        .ToListAsync();

    var currentIds = currentDevices
        .Select(x => x.Id)
        .ToHashSet();

    return currentDevices
        .Where(x => x.DeviceType == 1)
        .Select(x => x.Id)
        .Concat(legacyDevices
            .Where(x => !currentIds.Contains(x.Id))
            .Select(x => x.Id))
        .Distinct()
        .ToList();
}

/// <summary>
/// 内部查询定义
/// </summary>
private sealed class ReportRow
{
    public int ReportId { get; set; }
    public int PlanId { get; set; }
    public DateTime? ReportTime { get; set; }
    public string? ReportDetails { get; set; }
    public string? PlanDetails { get; set; }
    public int? TemplateId { get; set; }
}


/// <summary>
/// 查询过去报告
/// </summary>
private sealed class PlanRow
{
    public RehabPlanEntity Plan { get; set; } = new();
    public RehabTemplateEntity? Template { get; set; }
}

private async Task<List<ReportRow>> QueryReportsAsync(
    int patientId,
    DateTime start,
    DateTime end,
    List<int> deviceIds)
{
    if (deviceIds.Count == 0)
        return new();

    return await _reports.Db
        .Queryable<RehabReportEntity, RehabPlanEntity>(
            (report, plan) => new JoinQueryInfos(
                JoinType.Inner,
                report.PlanId == plan.Id))
        .Where((report, plan) =>
            plan.UserId == patientId &&
            plan.DeviceId != null &&
            deviceIds.Contains(plan.DeviceId.Value) &&
            report.CreateTime >= start &&
            report.CreateTime < end)
        .OrderBy((report, plan) =>
            report.CreateTime,
            OrderByType.Desc)
        .Select((report, plan) => new ReportRow
        {
            ReportId = report.Id,
            PlanId = plan.Id,
            ReportTime = report.CreateTime,
            ReportDetails = report.ReportDetails,
            PlanDetails = plan.Details,
            TemplateId = plan.TemplateId
        })
        .ToListAsync();
}

/// <summary>
/// 查询未来计划
/// </summary>
/// <param name="patientId"></param>
/// <param name="start"></param>
/// <param name="end"></param>
/// <param name="deviceIds"></param>
/// <returns></returns>

private async Task<List<PlanRow>> QueryFuturePlansAsync(
    int patientId,
    DateTime start,
    DateTime end,
    List<int> deviceIds)
{
    var plans = await _plans.Db.Queryable<RehabPlanEntity>()
        .Where(x =>
            x.UserId == patientId &&
            x.DeviceId != null &&
            deviceIds.Contains(x.DeviceId.Value) &&
            x.BookingTime >= start &&
            x.BookingTime < end)
        .OrderBy(x => x.BookingTime)
        .ToListAsync();

    var templateIds = plans
        .Where(x => x.TemplateId.HasValue)
        .Select(x => x.TemplateId!.Value)
        .Distinct()
        .ToList();

    var templates = templateIds.Count == 0
        ? new List<RehabTemplateEntity>()
        : await _templates.Db.Queryable<RehabTemplateEntity>()
            .Where(x => templateIds.Contains(x.Id))
            .ToListAsync();

    var lookup = templates.ToDictionary(x => x.Id);

    return plans.Select(plan => new PlanRow
    {
        Plan = plan,
        Template = plan.TemplateId.HasValue &&
                   lookup.TryGetValue(plan.TemplateId.Value, out var template)
            ? template
            : null
    }).ToList();
}

/// <summary>
/// 时间转换
/// </summary>
/// <param name="value"></param>
/// <returns></returns>
private static DateTimeOffset ToChinaOffset(DateTime value)
{
    var unspecified = DateTime.SpecifyKind(
        value,
        DateTimeKind.Unspecified);

    return new DateTimeOffset(
        unspecified,
        TimeSpan.FromHours(8));
}

private static TimeZoneInfo GetChinaTimeZone()
{
    try
    {
        return TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai");
    }
    catch (TimeZoneNotFoundException)
    {
        return TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");
    }
}

/// <summary>
/// 通用JSON解析方案，放置位置还要调整
/// </summary>
/// <param name="element"></param>
/// <param name="name"></param>
/// <param name="value"></param>
/// <returns></returns>
private static bool TryGetProperty(
    JsonElement element,
    string name,
    out JsonElement value)
{
    if (element.ValueKind == JsonValueKind.Object)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(
                    property.Name,
                    name,
                    StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }
    }

    value = default;
    return false;
}

private static string? GetString(
    JsonElement element,
    params string[] names)
{
    foreach (var name in names)
    {
        if (!TryGetProperty(element, name, out var value))
            continue;

        if (value.ValueKind == JsonValueKind.String)
            return value.GetString();

        if (value.ValueKind is JsonValueKind.Number
            or JsonValueKind.True
            or JsonValueKind.False)
        {
            return value.ToString();
        }
    }

    return null;
}

private static decimal? GetDecimal(
    JsonElement element,
    params string[] names)
{
    foreach (var name in names)
    {
        if (!TryGetProperty(element, name, out var value))
            continue;

        if (value.ValueKind == JsonValueKind.Number &&
            value.TryGetDecimal(out var number))
        {
            return number;
        }

        if (value.ValueKind == JsonValueKind.String &&
            decimal.TryParse(
                value.GetString(),
                NumberStyles.Any,
                CultureInfo.InvariantCulture,
                out number))
        {
            return number;
        }
    }

    return null;
}

private static int? GetInt(
    JsonElement element,
    params string[] names)
{
    var value = GetDecimal(element, names);

    if (!value.HasValue)
        return null;

    return decimal.ToInt32(value.Value);
}

/// <summary>
/// 患者资料解析
/// </summary>
private sealed class ParsedPatientDetails
{
    public string? Sex { get; set; }
    public int? Age { get; set; }
    public decimal? HeightCm { get; set; }
    public decimal? WeightKg { get; set; }
    public string? DiagnosisSummary { get; set; }
    public bool ParseSucceeded { get; set; }
}

private static ParsedPatientDetails ParsePatientDetails(
    string? json)
{
    var result = new ParsedPatientDetails();

    if (string.IsNullOrWhiteSpace(json))
        return result;

    try
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        if (root.ValueKind != JsonValueKind.Object)
            return result;

        result.Sex = GetString(root, "Sex", "sex");
        result.Age = GetInt(root, "Age", "age");
        result.HeightCm = GetDecimal(root, "Height", "height");
        result.WeightKg = GetDecimal(root, "Weight", "weight");

        var medicalHistory = GetString(
            root,
            "MedicalHistory",
            "medicalHistory");

        var rehabilitationDetails = GetString(
            root,
            "RehabilitationDetails",
            "rehabilitationDetails");

        var summaries = new[]
        {
            medicalHistory,
            rehabilitationDetails
        }
        .Where(x => !string.IsNullOrWhiteSpace(x))
        .Distinct()
        .ToList();

        if (summaries.Count > 0)
        {
            var text = string.Join(";", summaries);

            result.DiagnosisSummary = text.Length <= 500
                ? text
                : text[..500];
        }

        result.ParseSucceeded = true;
        return result;
    }
    catch (JsonException)
    {
        return result;
    }
}

/// <summary>
/// 训练计划解析
/// </summary>

private sealed class ParsedPlanDetails
{
    public decimal? PlannedMinutes { get; set; }
    public List<string> TaskNames { get; set; } = new();
    public bool ParseSucceeded { get; set; }
    public string Source { get; set; } = "missing";
}

private static ParsedPlanDetails ParsePlanArray(
    string? json,
    string source)
{
    var result = new ParsedPlanDetails
    {
        Source = source
    };

    if (string.IsNullOrWhiteSpace(json))
        return result;

    try
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        if (root.ValueKind != JsonValueKind.Array)
            return result;

        decimal totalMinutes = 0;
        var hasMinutes = false;

        foreach (var item in root.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                continue;

            var taskName = GetString(
                item,
                "GameNameCh",
                "gameNameCh",
                "GameCh",
                "gameCh",
                "gameName");

            if (!string.IsNullOrWhiteSpace(taskName))
                result.TaskNames.Add(taskName);

            var minutes = GetDecimal(item, "time");

            if (minutes.HasValue)
            {
                totalMinutes += minutes.Value;
                hasMinutes = true;
            }
        }

        result.TaskNames = result.TaskNames
            .Distinct()
            .ToList();

        result.PlannedMinutes = hasMinutes
            ? totalMinutes
            : null;

        result.ParseSucceeded = true;
        return result;
    }
    catch (JsonException)
    {
        return result;
    }
}

private static ParsedPlanDetails ParsePlanDetails(
    string? planDetails,
    string? templateDetails)
{
    var result = ParsePlanArray(
        planDetails,
        "dbrehaplan.Details");

    if (result.ParseSucceeded)
        return result;

    return ParsePlanArray(
        templateDetails,
        "dbtemplates.Details");
}

/// <summary>
/// 报告JSON解析
/// </summary>
private sealed class ParsedReportDetails
{
    public decimal? DurationMinutes { get; set; }
    public int? TaskCount { get; set; }
    public decimal? WalkingDistanceMeters { get; set; }
    public int? SitToStandCount { get; set; }
    public bool ParseSucceeded { get; set; }
    public List<string> Notes { get; set; } = new();
}

private static ParsedReportDetails ParseReportDetails(
    string? json)
{
    var result = new ParsedReportDetails();

    if (string.IsNullOrWhiteSpace(json))
    {
        result.Notes.Add("报告详情为空");
        return result;
    }

    try
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        if (!TryGetProperty(
                root,
                "ReportList",
                out var reportList) ||
            reportList.ValueKind != JsonValueKind.Array)
        {
            result.Notes.Add("报告不包含可解析的ReportList");
            return result;
        }

        decimal duration = 0;
        decimal distance = 0;
        int sitToStand = 0;

        var hasDuration = false;
        var hasDistance = false;
        var hasSitToStand = false;

        foreach (var item in reportList.EnumerateArray())
        {
            foreach (var field in new[]
            {
                "GTrainingTime",
                "WTrainingWalktime",
                "STrainingSitTime",
                "RTrainingBalanceTime"
            })
            {
                var value = GetDecimal(item, field);

                if (value.HasValue)
                {
                    duration += value.Value;
                    hasDuration = true;
                }
            }

            var itemDistance = GetDecimal(item, "WDistance");

            if (itemDistance.HasValue)
            {
                distance += itemDistance.Value;
                hasDistance = true;
            }

            var itemCount = GetInt(item, "SNumber");

            if (itemCount.HasValue)
            {
                sitToStand += itemCount.Value;
                hasSitToStand = true;
            }
        }

        result.TaskCount = reportList.GetArrayLength();

        result.DurationMinutes = hasDuration
            ? duration
            : null;

        result.WalkingDistanceMeters = hasDistance
            ? distance
            : null;

        result.SitToStandCount = hasSitToStand
            ? sitToStand
            : null;

        result.ParseSucceeded = true;
        return result;
    }
    catch (JsonException)
    {
        result.Notes.Add("报告详情JSON格式无法解析");
        return result;
    }
}
}