using AI_WebApi.IServices.AITools;
using AI_WebApi.Model.Domain.IReGo;
using AI_WebApi.Model.DTOs.AITools;
using AI_WebApi.Model.Entities;
using AI_WebApi.Repository.AITools;
using Newtonsoft.Json.Linq;
using System.Globalization;

namespace AI_WebApi.Services.AITools;

/// <summary>Three independent patient data layers; no multisource endpoint dependency.</summary>
public sealed class PatientLayerService : IPatientLayerService
{
    private readonly IPatientLayerRepository _repository;
    private readonly IIReTourPatientHistoryService _tour;
    private readonly IIReGoPatientHistoryService _go;

    public PatientLayerService(IPatientLayerRepository repository,
        IIReTourPatientHistoryService tour, IIReGoPatientHistoryService go)
    {
        _repository = repository; _tour = tour; _go = go;
    }

    private static ToolMetaDto Meta() => new()
    {
        SourceCompleteness = "project_primary",
        RegistryVersions = new RegistryVersionsDto { Contract = PatientLayerContract.Version }
    };

    private static ToolResponseDto<T> Envelope<T>(string tool, string rid, T? data,
        string status = ToolStatuses.Success, IEnumerable<string>? reasons = null,
        string? message = null) => ToolResponseDto<T>.Create(tool, rid, status, data,
        reasons, Meta(), message, PatientLayerContract.Version);

    private async Task<PatientEntity?> PatientAsync(TrustedPatientContext context, CancellationToken ct)
    {
        var patient = await _repository.PatientAsync(context, ct);
        return patient?.IsDeleted == true ? null : patient;
    }

    private static PatientLayerPageDto Page(int number, int size, int count) => new()
    {
        PageNumber = number, PageSize = size, TotalItems = count, HasNext = number * size < count
    };

    public static IReadOnlyDictionary<string, object?> ProfileFacts(string? json)
    {
        var result = new Dictionary<string, object?>();
        try
        {
            var root = JObject.Parse(json ?? "{}");
            var info = root["patientInfo"] as JObject ?? root;
            var fields = new Dictionary<string, string>
            {
                ["age"] = "age", ["sex"] = "sex", ["gender"] = "gender",
                ["height"] = "height_cm", ["weight"] = "weight_kg",
                ["medicalHistory"] = "self_reported_medical_history",
                ["pastHistory"] = "self_reported_past_history",
                ["allergyHistory"] = "self_reported_allergy_history",
                ["diagnosis"] = "profile_diagnosis", ["familyHistory"] = "self_reported_family_history",
                ["rehabilitationDetails"] = "rehabilitation_details", ["degree"] = "education",
                ["birthday"] = "birth_date", ["operationTime"] = "operation_date"
            };
            foreach (var field in fields)
            {
                var token = info.GetValue(field.Key, StringComparison.OrdinalIgnoreCase);
                if (token == null || token.Type == JTokenType.Null || string.IsNullOrWhiteSpace(token.ToString()))
                    token = root.GetValue(field.Key, StringComparison.OrdinalIgnoreCase);
                if (token == null || token.Type == JTokenType.Null || string.IsNullOrWhiteSpace(token.ToString())) continue;
                if (field.Key is "age" or "height" or "weight")
                {
                    if (!decimal.TryParse(token.ToString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var number) || number <= 0) continue;
                    result[field.Value] = number;
                }
                else if (field.Key is "birthday" or "operationTime")
                {
                    if (DateTime.TryParse(token.ToString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) && date.Year > 1900)
                        result[field.Value] = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                }
                else result[field.Value] = Clip(token.ToString());
            }
            if (info.GetValue("injuryDetails", StringComparison.OrdinalIgnoreCase) is JObject injury)
            {
                var details = new Dictionary<string, object?>();
                if (injury.GetValue("state", StringComparison.OrdinalIgnoreCase) is JValue state && !string.IsNullOrWhiteSpace(state.ToString()))
                    details["description"] = Clip(state.ToString());
                if (int.TryParse(injury.GetValue("injuredArea", StringComparison.OrdinalIgnoreCase)?.ToString(), out var area) && area > 0)
                    details["area_code"] = area;
                if (DateTime.TryParse(injury.GetValue("injuredTime", StringComparison.OrdinalIgnoreCase)?.ToString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) && date.Year > 1900)
                    details["injured_on"] = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                if (details.Count > 0) result["injury_details"] = details;
            }
        }
        catch (Newtonsoft.Json.JsonException) { }
        return result;
    }

    public static IReadOnlyDictionary<string, object?> CareContextFacts(string? json, int institutionId)
    {
        var result = new Dictionary<string, object?>();
        try
        {
            var root = JObject.Parse(json ?? "{}");
            if (root.GetValue("patientInfo", StringComparison.OrdinalIgnoreCase) is not JObject info ||
                !int.TryParse(info.GetValue("hospitalId", StringComparison.OrdinalIgnoreCase)?.ToString(), out var hospital) || hospital != institutionId)
                return result;
            foreach (var pair in new Dictionary<string, string>
            { ["departmentName"] = "department_name", ["roomNumber"] = "room_number", ["bedNumber"] = "bed_number", ["patientState"] = "patient_state" })
            {
                var value = info.GetValue(pair.Key, StringComparison.OrdinalIgnoreCase);
                if (value is JValue && !string.IsNullOrWhiteSpace(value.ToString())) result[pair.Value] = Clip(value.ToString());
            }
            if (DateTime.TryParse(info.GetValue("inDate", StringComparison.OrdinalIgnoreCase)?.ToString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) && date.Year > 1900)
                result["admitted_on"] = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }
        catch (Newtonsoft.Json.JsonException) { }
        return result;
    }

    public static IReadOnlyDictionary<string, object?> MedicalFacts(string? json)
    {
        var result = new Dictionary<string, object?>();
        try
        {
            foreach (var field in JObject.Parse(json ?? "{}"))
            {
                var key = field.Key switch
                {
                    "DiseaseName" => "diagnosis", "DoctorOrder" => "doctor_orders",
                    "DiagnosisResult" => "diagnosis_result", _ => field.Key
                };
                result[key] = field.Value is JValue value ? value.Value : field.Value?.DeepClone();
                if (result[key] is string text) result[key] = Clip(text);
            }
        }
        catch (Newtonsoft.Json.JsonException)
        {
            result["unparsed_content"] = Clip(json);
        }
        return result;
    }

    private static string? Clip(string? value) => value?.Length > 6000 ? value[..6000] + "（内容已截断）" : value;

    public async Task<ToolResponseDto<PatientProfileDto>> ProfileAsync(
        TrustedPatientContext context, string requestId, CancellationToken ct)
    {
        var patient = await PatientAsync(context, ct);
        if (patient == null) return Envelope<PatientProfileDto>("get_patient_profile", requestId, null,
            ToolStatuses.Unavailable, new[] { "PROJECT_PATIENT_NOT_FOUND" }, "未找到该患者的基本信息。");
        var facts = ProfileFacts(patient.UserDetails);
        var limitations = new List<string>();
        if (facts.Count == 0) limitations.Add("个人档案未填写可读取的基本信息；不代表没有病史。");
        if (patient.Status == -1) limitations.Add("这是历史账号的记录。");
        return Envelope("get_patient_profile", requestId, new PatientProfileDto
        {
            PatientRef = context.PatientRef, Profile = facts, Limitations = limitations,
            CareContext = context.Access?.DataScope == "current_tenant" && context.Access.CurrentBusinessId is > 0
                ? CareContextFacts(patient.UserDetails, context.Access.CurrentBusinessId.Value) : new Dictionary<string, object?>()
        });
    }

    public async Task<ToolResponseDto<PatientConsultationDto>> ConsultationAsync(
        TrustedPatientContext context, PatientLayerRequestDto request, string requestId, CancellationToken ct)
    {
        if (await PatientAsync(context, ct) == null)
            return Envelope<PatientConsultationDto>("get_patient_consultation", requestId, null,
                ToolStatuses.Unavailable, new[] { "PROJECT_PATIENT_NOT_FOUND" }, "未找到该患者。");
        var medical = await _repository.MedicalAsync(context, request.PageNumber, request.PageSize, ct);
        var consultations = await _repository.ConsultationsAsync(context, request.PageNumber, request.PageSize, ct);
        var records = new List<PatientMedicalRecordDto>();
        foreach (var row in medical.Rows)
        {
            var prescriptions = await _repository.PrescriptionsAsync(context, row.RecordId, ct);
            records.Add(new PatientMedicalRecordDto
            {
                RecordId = row.RecordId, RecordedAt = row.RecordedAt, RecordTenant = row.RecordTenant,
                MedicalRecord = Clip(row.MedicalRecord), Content = MedicalFacts(row.MedicalContent),
                Summary = Clip(row.Summary), FollowupTime = row.FollowupTime,
                FollowupHospital = row.FollowupHospital, Prescriptions = prescriptions
            });
        }
        return Envelope("get_patient_consultation", requestId, new PatientConsultationDto
        {
            PatientRef = context.PatientRef, MedicalRecordCount = medical.Count,
            ConsultationCount = consultations.Count, MedicalRecords = records,
            Consultations = consultations.Rows,
            Page = Page(request.PageNumber, request.PageSize, Math.Max(medical.Count, consultations.Count)),
            Limitations = medical.Count == 0 ? new[] { "当前授权范围没有可读取的有效病历记录；不能据此判断没有病史。" } : Array.Empty<string>()
        });
    }

    public async Task<ToolResponseDto<PatientRehabDto>> RehabAsync(
        TrustedPatientContext context, PatientLayerRequestDto request, string requestId, CancellationToken ct)
    {
        if (await PatientAsync(context, ct) == null)
            return Envelope<PatientRehabDto>("get_patient_rehab", requestId, null,
                ToolStatuses.Unavailable, new[] { "PROJECT_PATIENT_NOT_FOUND" }, "未找到该患者。");
        var plans = await _repository.PlansAsync(context, request.PageNumber, request.PageSize, ct);
        var reports = await _repository.ReportsAsync(context, request.PageNumber, request.PageSize, ct);
        PatientRehabDeviceDto tour;
        PatientRehabDeviceDto go;
        try
        {
            var response = await _tour.GetAsync(context, new GetIReTourPatientHistoryRequestDto
            { PageNumber = request.PageNumber, PageSize = request.PageSize }, requestId, ct);
            tour = Device(response.Status, response.Data, response.ReasonCodes, response.PatientMessage);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception) { tour = Device(ToolStatuses.Failed, null, new[] { "IRETOUR_DEPENDENCY_FAILURE" }, "IReTour记录暂时无法读取。"); }
        try
        {
            var response = await _go.GetAsync(context, new GetIReGoPatientHistoryRequestDto
            { PageNumber = request.PageNumber, PageSize = request.PageSize }, requestId, ct);
            go = Device(response.Status, response.Data, response.ReasonCodes, response.PatientMessage);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception) { go = Device(ToolStatuses.Failed, null, new[] { "IREGO_DEPENDENCY_FAILURE" }, "IReGo记录暂时无法读取。"); }
        var failed = tour.Availability == ToolStatuses.Failed || go.Availability == ToolStatuses.Failed;
        return Envelope("get_patient_rehab", requestId, new PatientRehabDto
        {
            PatientRef = context.PatientRef, PlanCount = plans.Count, Plans = plans.Rows,
            PlanPage = Page(request.PageNumber, request.PageSize, plans.Count),
            ReportCount = reports.Count, Reports = reports.Rows, ReportPage = Page(request.PageNumber, request.PageSize, reports.Count),
            Iretour = tour, Irego = go,
            Limitations = new[] { "仅包含可确认属于本账号的记录；未映射的步道和微信记录不纳入，不代表没有这些记录。", "历史训练结果不代表当前状态；计划完成标记不能替代训练结果。" }
        }, failed ? ToolStatuses.Partial : ToolStatuses.Success);
    }

    private static PatientRehabDeviceDto Device(string status, object? history,
        IReadOnlyList<string> reasons, string? message) => new()
    { Availability = status, History = history, ReasonCodes = reasons, PatientMessage = message };
}
