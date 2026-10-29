using AI_WebApi.IServices.AITools;
using AI_WebApi.Model.Domain.IReGo;
using AI_WebApi.Model.DTOs.AITools;
using AI_WebApi.Model.Entities;
using AI_WebApi.Repository.AITools;
using Newtonsoft.Json.Linq;

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
        var patient = await _repository.PatientAsync(context.PatientId, ct);
        return patient?.IsDeleted == true ? null : patient;
    }

    private static string Ref(int id) => $"project:{id}";
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
                ["diagnosis"] = "profile_diagnosis"
            };
            foreach (var field in fields)
            {
                var token = info.GetValue(field.Key, StringComparison.OrdinalIgnoreCase)
                    ?? root.GetValue(field.Key, StringComparison.OrdinalIgnoreCase);
                if (token == null || token.Type == JTokenType.Null || string.IsNullOrWhiteSpace(token.ToString())) continue;
                if (field.Key is "age" or "height" or "weight")
                {
                    if (!decimal.TryParse(token.ToString(), out var number) || number <= 0) continue;
                    result[field.Value] = number;
                }
                else result[field.Value] = Clip(token.ToString());
            }
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
            PatientRef = Ref(patient.Id), Profile = facts, Limitations = limitations
        });
    }

    public async Task<ToolResponseDto<PatientConsultationDto>> ConsultationAsync(
        TrustedPatientContext context, PatientLayerRequestDto request, string requestId, CancellationToken ct)
    {
        if (await PatientAsync(context, ct) == null)
            return Envelope<PatientConsultationDto>("get_patient_consultation", requestId, null,
                ToolStatuses.Unavailable, new[] { "PROJECT_PATIENT_NOT_FOUND" }, "未找到该患者。");
        var medical = await _repository.MedicalAsync(context.PatientId, request.PageNumber, request.PageSize, ct);
        var consultations = await _repository.ConsultationsAsync(context.PatientId, request.PageNumber, request.PageSize, ct);
        var records = new List<PatientMedicalRecordDto>();
        foreach (var row in medical.Rows)
        {
            var prescriptions = await _repository.PrescriptionsAsync(context.PatientId, row.RecordId, ct);
            records.Add(new PatientMedicalRecordDto
            {
                RecordId = row.RecordId, RecordedAt = row.RecordedAt,
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
            Limitations = medical.Count == 0 ? new[] { "当前数据库没有该患者的病历记录；不能据此判断没有病史。" } : Array.Empty<string>()
        });
    }

    public async Task<ToolResponseDto<PatientRehabDto>> RehabAsync(
        TrustedPatientContext context, PatientLayerRequestDto request, string requestId, CancellationToken ct)
    {
        if (await PatientAsync(context, ct) == null)
            return Envelope<PatientRehabDto>("get_patient_rehab", requestId, null,
                ToolStatuses.Unavailable, new[] { "PROJECT_PATIENT_NOT_FOUND" }, "未找到该患者。");
        var plans = await _repository.PlansAsync(context, request.PageNumber, request.PageSize, ct);
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
            PlanPage = Page(request.PageNumber, request.PageSize, plans.Count), Iretour = tour, Irego = go,
            Limitations = new[] { "仅包含已记录的计划和设备训练；未接入的评估或其他康复内容不能视为不存在。", "历史训练结果不代表当前状态；计划完成标记不能替代训练结果。" }
        }, failed ? ToolStatuses.Partial : ToolStatuses.Success);
    }

    private static PatientRehabDeviceDto Device(string status, object? history,
        IReadOnlyList<string> reasons, string? message) => new()
    { Availability = status, History = history, ReasonCodes = reasons, PatientMessage = message };
}
