using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using AI_WebApi.IServices.AITools;
using AI_WebApi.Model.AITools;
using AI_WebApi.Model.Domain.IReGo;
using AI_WebApi.Model.DTOs.AITools;
using AI_WebApi.Model.Entities;
using AI_WebApi.Repository.AITools;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AI_WebApi.Services.AITools.Assessment;

public sealed class AssessmentToolService : IAssessmentToolService
{
    private readonly IAssessmentDataRepository _repository;
    private readonly AssessmentToolOptions _options;
    private readonly IIReGoReportImageRenderer _renderer;
    private readonly IIReGoReportArtifactStore _artifacts;
    private readonly IReGoReportImageOptions _images;
    private static readonly string[] Limitations =
    {
        "总分为源报告记录，未重新计算题目得分；题目、答案及自由文本解释不在本工具中展示。",
        "量表版本、计分方向和规则尚未核验；不判断正常异常、诊断或疗效，不跨量表平均或求和。",
        "报告上传时间与测评实际发生时间可能不同；计划日期不代表已完成测评。"
    };

    public AssessmentToolService(IAssessmentDataRepository repository, IOptions<AssessmentToolOptions> options,
        IIReGoReportImageRenderer renderer, IIReGoReportArtifactStore artifacts, IOptions<IReGoToolOptions> images)
    { _repository = repository; _options = options.Value; _renderer = renderer; _artifacts = artifacts; _images = images.Value.ReportImage; }

    public async Task<ToolResponseDto<AssessmentToolDataDto>> ReadAsync(string operation, TrustedPatientContext context,
        AssessmentToolRequestDto request, string requestId, CancellationToken cancellationToken)
    {
        var tool = "get_assessment_" + operation;
        ToolResponseDto<AssessmentToolDataDto> Fail(string code, string message) => Response<AssessmentToolDataDto>(tool, requestId, ToolStatuses.Unavailable, null, new[] { code }, message);
        if (!Valid(request, operation)) return Fail("ASSESSMENT_INVALID_REQUEST", "查询参数不符合量表工具契约。");
        if (_options.ReferenceSigningKey.Length < 32) return Fail("ASSESSMENT_SIGNING_KEY_NOT_CONFIGURED", "量表工具尚未配置引用签名。");
        var source = await _repository.LoadAsync(context, cancellationToken);
        var patient = source.Patient;
        if (patient == null || patient.Id != context.PatientId || patient.Status != 1 || patient.IsDeleted != 0 || patient.BusinessId is not > 0)
            return Fail("ASSESSMENT_PATIENT_SCOPE_UNAVAILABLE", "无法确认当前患者数据范围。");
        var projected = new List<Projected>();
        foreach (var plan in source.Plans.Where(p => p.UserId == patient.Id && p.BusinessId == patient.BusinessId &&
            p.DeviceId == -1 && p.TemplateId is 0 or -1))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var item = Project(plan, source.Reports.Where(r => r.PlanId == plan.Id).OrderBy(r => r.Id).ToArray(), patient.BusinessId.Value);
            if (item != null) projected.Add(item);
        }
        var rows = projected.Where(p => request.ScaleType == null || p.Record.ScaleType == request.ScaleType)
            .OrderByDescending(p => p.Record.RecordedAt).ThenBy(p => p.Record.AssessmentRef, StringComparer.Ordinal).ToArray();
        Projected[] selected;
        if (operation == "patient_context") selected = Array.Empty<Projected>();
        else if (operation == "patient_history") selected = rows.Skip((request.PageNumber - 1) * request.PageSize).Take(request.PageSize).ToArray();
        else if (operation == "result_analysis")
        {
            var match = request.Selector == "assessment_ref"
                ? rows.SingleOrDefault(p => FixedEqual(p.Record.AssessmentRef, request.AssessmentRef!))
                : request.Selector == "latest_usable" ? rows.FirstOrDefault(p => p.Record.ReportedScore.HasValue) : rows.FirstOrDefault();
            if (match == null) return Fail("ASSESSMENT_REF_NOT_FOUND", "没有符合条件的量表记录。");
            selected = new[] { match };
        }
        else
        {
            selected = (request.SelectionMode == "date_range"
                ? rows.Where(p => p.Record.RecordedAt >= request.StartDate && p.Record.RecordedAt <= request.EndDate)
                : rows.Take(request.ReportCount)).OrderBy(p => p.Record.RecordedAt).ThenBy(p => p.Record.AssessmentRef, StringComparer.Ordinal).ToArray();
            if (selected.Length is < 2 or > 20) return Fail("ASSESSMENT_INVALID_WINDOW", "请选择 2 至 20 条记录；缺失结果保留，日期范围不自动截断。");
        }
        var records = selected.Select(p => p.Record).ToArray();
        var reasons = records.SelectMany(r => r.ReasonCodes).Distinct().ToList();
        var contextRead = operation == "patient_context";
        if (contextRead) reasons.AddRange(rows.SelectMany(p => p.Record.ReasonCodes).Distinct());
        var data = new AssessmentToolDataDto
        {
            Counts = new Dictionary<string, int>
            {
                ["record_count"] = rows.Length, ["readable_total_count"] = rows.Count(p => p.Record.ReportedScore.HasValue),
                ["missing_result_count"] = rows.Count(p => p.Record.ResultState == "no_result"),
                ["blocked_result_count"] = rows.Count(p => p.Record.ResultState != "no_result" && !p.Record.ReportedScore.HasValue)
            },
            ScaleCoverage = contextRead ? AssessmentScaleCatalog.Names.Select(pair => new AssessmentScaleCoverageDto
            {
                ScaleType = pair.Key, ScaleName = pair.Value,
                RecordCount = rows.Count(p => p.Record.ScaleType == pair.Key),
                ReadableTotalCount = rows.Count(p => p.Record.ScaleType == pair.Key && p.Record.ReportedScore.HasValue)
            }).ToArray() : Array.Empty<AssessmentScaleCoverageDto>(),
            Records = records, PageNumber = operation == "patient_history" ? request.PageNumber : 0,
            HasNext = operation == "patient_history" && (long)request.PageNumber * request.PageSize < rows.Length,
            Comparability = operation == "longitudinal_analysis" ? "blocked_unverified_scoring_rules" : "not_requested",
            Interpretation = new()
            {
                Method = "source_assessment_reading_v1",
                Summary = contextRead
                    ? $"当前范围有 {rows.Length} 条线上记录，其中 {rows.Count(p => p.Record.ReportedScore.HasValue)} 条可读取源总分。"
                    : $"所选范围有 {records.Length} 条量表记录，其中 {records.Count(r => r.ReportedScore.HasValue)} 条可读取源总分。",
                Observations = operation is "patient_context" or "patient_history" ? Array.Empty<string>() : records.Select(r =>
                    $"{r.ScaleName ?? "待识别量表"}：{(r.ReportedScore.HasValue ? "源报告总分 " + r.ReportedScore.Value.ToString(CultureInfo.InvariantCulture) : "缺少可核验总分，不能当作零分")}。").ToArray(),
                Limitations = Limitations,
                NextChecks = new[] { "先核对量表版本及计分规则，再开放同量表数值变化；来源冲突或缺失时核对原始记录。" }
            }
        };
        var snapshotRows = contextRead ? rows : selected;
        data.SourceSnapshot = Hash(JsonConvert.SerializeObject(new
        {
            contract = AssessmentToolOptions.ContractVersion, catalog = AssessmentScaleCatalog.Version,
            patient.Id, patient.BusinessId, operation, scale_filter = operation == "result_analysis" ? null : request.ScaleType,
            records = snapshotRows.Select(p => new { p.Record, p.Revision }), data.Interpretation, data.Comparability
        }));
        if (operation == "longitudinal_analysis") reasons.Add("ASSESSMENT_SCORING_RULES_UNVERIFIED");
        return Response(tool, requestId, reasons.Count == 0 ? ToolStatuses.Success : ToolStatuses.Partial, data, reasons);
    }

    private Projected? Project(RehabPlanEntity plan, RehabReportEntity[] reports, int institution)
    {
        var reasons = new List<string>();
        int? type = null;
        string? planName = null;
        try
        {
            var metadata = ReadJson(plan.Details);
            if (metadata["AccessType"] is JObject nested) metadata = nested;
            if (int.TryParse(metadata["type"]?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var code) && AssessmentScaleCatalog.Names.ContainsKey(code)) type = code;
            planName = metadata["name"]?.Type == JTokenType.String ? metadata.Value<string>("name")?.Trim() : null;
        }
        catch (JsonException) { reasons.Add("ASSESSMENT_PLAN_SCHEMA_UNSUPPORTED"); }
        if (planName != null && AssessmentScaleCatalog.OnlineGames.Contains(planName)) return null;
        var planIdentity = type.HasValue && AssessmentScaleCatalog.Matches(type.Value, planName);
        if (!type.HasValue) reasons.Add("ASSESSMENT_SCALE_UNIDENTIFIED");
        else if (!string.IsNullOrEmpty(planName) && !planIdentity) reasons.Add("ASSESSMENT_PLAN_CATALOG_CONFLICT");
        else if (string.IsNullOrEmpty(planName)) reasons.Add("ASSESSMENT_PLAN_NAME_MISSING");
        var state = "no_result";
        var schema = "unrecognized";
        var identity = planIdentity ? "plan_catalog_only" : "unverified";
        decimal? score = null;
        string? scoreField = null;
        if (reports.Length > 0)
        {
            try
            {
                if (reports.All(r => string.IsNullOrWhiteSpace(r.ReportDetails))) reasons.Add("ASSESSMENT_RESULT_MISSING");
                else
                {
                    var parsed = reports.Select(r => ReadJson(r.ReportDetails)).ToArray();
                    if (parsed.Skip(1).Any(r => !JToken.DeepEquals(parsed[0], r)))
                    { state = "conflicting"; reasons.Add("ASSESSMENT_REPORT_CONFLICT"); }
                    else
                    {
                        var json = parsed[0];
                        var lower = json["score"] != null;
                        var scoreToken = json[lower ? "score" : "Score"];
                        var title = json[lower ? "tableTitle" : "TableTitle"];
                        var questions = json[lower ? "title" : "Title"];
                        if (scoreToken?.Type is not (JTokenType.Float or JTokenType.Integer or JTokenType.String) || questions is not JArray a || a.Count is < 1 or > 500 ||
                            (json["score"] != null && json["Score"] != null) || title?.Type != JTokenType.String)
                            throw new JsonReaderException("Unsupported assessment summary");
                        schema = lower ? "lowercase_total_v1" : json["OptionsScore"] != null ? "options_score_total_v1" : json["OptionScore"] != null ? "option_score_total_v1" : "observed_total_v1";
                        if (!type.HasValue || (!string.IsNullOrEmpty(planName) && !planIdentity) || !AssessmentScaleCatalog.Matches(type.Value, title.Value<string>()))
                        { state = "identity_conflict"; reasons.Add("ASSESSMENT_REPORT_IDENTITY_UNVERIFIED"); }
                        else if (!decimal.TryParse(scoreToken.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                        { state = "invalid_score"; reasons.Add("ASSESSMENT_INVALID_SCORE"); }
                        else
                        {
                            identity = "catalog_and_report_match"; score = number; state = "reported_total_unverified";
                            scoreField = "dbreport.ReportDetails." + (lower ? "score" : "Score");
                            reasons.Add("ASSESSMENT_SCORE_SEMANTICS_UNVERIFIED");
                        }
                    }
                }
            }
            catch (JsonException) { state = "unsupported_schema"; reasons.Add("ASSESSMENT_REPORT_SCHEMA_UNSUPPORTED"); }
        }
        else reasons.Add("ASSESSMENT_RESULT_MISSING");
        var uploadTime = reports.Select(r => Time(r.CreateTime)).Where(t => t.HasValue).OrderByDescending(t => t).FirstOrDefault();
        var record = new AssessmentRecordDto
        {
            AssessmentRef = "assessment_s_v1_" + Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(_options.ReferenceSigningKey), Encoding.UTF8.GetBytes($"assessment|project|{institution}|{plan.UserId}|{plan.Id}"))).ToLowerInvariant(),
            ScaleType = !string.IsNullOrEmpty(planName) && !planIdentity ? null : type,
            ScaleName = type.HasValue && (planIdentity || identity == "catalog_and_report_match") ? AssessmentScaleCatalog.Names[type.Value] : null,
            IdentityStatus = identity, AssessmentOrigin = plan.DoctorId is not > 0 ? "unknown" : plan.DoctorId == plan.UserId ? "self" : "clinician_assigned",
            RecordedAt = uploadTime ?? Time(plan.BookingTime) ?? Time(plan.CreateTime),
            TimeMeaning = uploadTime.HasValue ? "report_uploaded_at" : Time(plan.BookingTime).HasValue ? "plan_scheduled_at" : "plan_created_at",
            ResultState = state, ReportedScore = score, SourceScoreField = scoreField, SchemaFamily = schema, ReasonCodes = reasons
        };
        return new(record, Hash(JsonConvert.SerializeObject(new { plan.Details, plan.BookingTime, plan.CreateTime, plan.DoctorId, reports = reports.Select(r => new { r.Id, r.ReportDetails, r.CreateTime, r.UpdateTime }) })));
    }

    public async Task<ToolResponseDto<AssessmentReportArtifactDto>> ReportAsync(bool longitudinal, TrustedPatientContext context,
        AssessmentToolRequestDto request, string baseUrl, string requestId, CancellationToken cancellationToken)
    {
        var tool = longitudinal ? "generate_assessment_longitudinal_report" : "generate_assessment_single_report";
        if (request.ExpectedSourceSnapshot is not { Length: 64 } || (!longitudinal && request.Selector != "assessment_ref"))
            return Response<AssessmentReportArtifactDto>(tool, requestId, ToolStatuses.Unavailable, null, new[] { "ASSESSMENT_ANALYSIS_REQUIRED" }, "请先分析同一记录或同一范围，再生成报表。");
        var analysis = await ReadAsync(longitudinal ? "longitudinal_analysis" : "result_analysis", context, request, requestId, cancellationToken);
        if (analysis.Data == null || analysis.Data.Records.Count == 0)
            return Response<AssessmentReportArtifactDto>(tool, requestId, ToolStatuses.Unavailable, null, analysis.ReasonCodes);
        var data = analysis.Data;
        if (!FixedEqual(data.SourceSnapshot, request.ExpectedSourceSnapshot))
            return Response<AssessmentReportArtifactDto>(tool, requestId, ToolStatuses.Unavailable, null, new[] { "ASSESSMENT_SOURCE_CHANGED" }, "数据已变化，请重新分析后生成报表。");
        if (!_artifacts.IsConfigured)
            return Response<AssessmentReportArtifactDto>(tool, requestId, ToolStatuses.Unavailable, null, new[] { "ASSESSMENT_REPORT_STORE_NOT_CONFIGURED" });
        var reference = _artifacts.CreateArtifactRef(context.PatientRef, "assessment:" + string.Join("|", data.Records.Select(r => r.AssessmentRef)), data.SourceSnapshot, _options.ReportTemplateVersion);
        if (!_artifacts.TryGet(reference, out _))
        {
            try { _artifacts.Store(reference, await _renderer.RenderPngAsync(BuildHtml(data), cancellationToken)); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception) { return Response<AssessmentReportArtifactDto>(tool, requestId, ToolStatuses.Unavailable, null, new[] { "ASSESSMENT_RENDERING_UNAVAILABLE" }); }
        }
        var origin = string.IsNullOrWhiteSpace(_images.PublicBaseUrl) ? baseUrl : _images.PublicBaseUrl;
        var url = origin.TrimEnd('/') + "/api/ai/patients/report-artifacts/" + Uri.EscapeDataString(reference) + "/content";
        return Response(tool, requestId, analysis.Status, new AssessmentReportArtifactDto
        {
            ArtifactRef = reference, ImageUrl = url, DisplayMarkdown = $"![线上量表记录]({url})", SourceSnapshot = data.SourceSnapshot,
            TemplateVersion = _options.ReportTemplateVersion, StorageMode = _artifacts.StorageMode
        }, analysis.ReasonCodes);
    }

    public static string BuildHtml(AssessmentToolDataDto data)
    {
        static string E(string? value) => WebUtility.HtmlEncode(value ?? "未记录");
        var html = new StringBuilder("<!doctype html><html lang=\"zh-CN\"><meta charset=\"utf-8\"><title>线上量表报告</title><style>body{margin:0;padding:40px;background:#edf3f5;color:#183344;font:17px/1.7 sans-serif}main{max-width:1100px;margin:auto;padding:40px;background:white;border-radius:20px}header{border-bottom:4px solid #137d86}table{border-collapse:collapse;width:100%;margin:25px 0;table-layout:fixed}th,td{padding:14px;text-align:left;border-bottom:1px solid #d9e4e8;overflow-wrap:anywhere}.note{padding:20px;background:#f2f6f8;border-radius:12px}small{overflow-wrap:anywhere;color:#607680}</style><main><header><small>ONLINE ASSESSMENT</small><h1>线上量表记录报告</h1></header><p>");
        html.Append(E(data.Interpretation.Summary)).Append("</p><table><tr><th>记录时间与来源</th><th>量表</th><th>源总分</th><th>结果状态</th></tr>");
        foreach (var r in data.Records)
            html.Append("<tr><td>").Append(E(r.RecordedAt?.ToString("yyyy-MM-dd HH:mm"))).Append("<br><small>")
                .Append(r.TimeMeaning == "report_uploaded_at" ? "报告上传时间" : "计划时间，非测评完成时间").Append("</small></td><td>")
                .Append(E(r.ScaleName)).Append("</td><td>").Append(E(r.ReportedScore?.ToString(CultureInfo.InvariantCulture))).Append("</td><td>")
                .Append(r.ResultState switch { "reported_total_unverified" => "有源总分，计分语义待核验", "no_result" => "缺少结果", "conflicting" => "报告内容冲突", "identity_conflict" => "量表身份待核对", "invalid_score" => "分数无效", _ => "结构尚未支持" }).Append("</td></tr>");
        html.Append("</table><section class=\"note\"><h2>解读范围与待核对事项</h2><ul>");
        foreach (var note in data.Interpretation.Limitations.Concat(data.Interpretation.NextChecks)) html.Append("<li>").Append(E(note)).Append("</li>");
        return html.Append("</ul></section><p><small>数据快照 ").Append(E(data.SourceSnapshot)).Append("</small></p></main></html>").ToString();
    }

    private sealed record Projected(AssessmentRecordDto Record, string Revision);
    private static JObject ReadJson(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || raw.Length > 524288) throw new JsonReaderException("Invalid source size");
        using var reader = new JsonTextReader(new StringReader(raw)) { MaxDepth = 32, FloatParseHandling = FloatParseHandling.Decimal };
        var result = JObject.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
        if (reader.Read()) throw new JsonReaderException("Trailing source content");
        return result;
    }
    private static bool Valid(AssessmentToolRequestDto r, string operation) =>
        operation is "patient_context" or "patient_history" or "result_analysis" or "longitudinal_analysis" &&
        r.PageNumber is >= 1 and <= 100000 && r.PageSize is >= 1 and <= 50 && (r.ScaleType == null || r.ScaleType is >= 1 and <= 13) &&
        r.Selector is "latest_record" or "latest_usable" or "assessment_ref" &&
        (r.Selector != "assessment_ref" || r.AssessmentRef is { Length: > 0 and <= 100 }) &&
        (operation != "longitudinal_analysis" || (r.SelectionMode == "latest_count" && r.ReportCount is >= 2 and <= 20 && r.StartDate == null && r.EndDate == null ||
            r.SelectionMode == "date_range" && r.StartDate.HasValue && r.EndDate.HasValue && r.StartDate <= r.EndDate && (r.EndDate.Value - r.StartDate.Value).TotalDays <= 180));
    private static DateTimeOffset? Time(DateTime? value) => value is { Year: >= 1900 } v ? new DateTimeOffset(DateTime.SpecifyKind(v, DateTimeKind.Unspecified), TimeSpan.FromHours(8)) : null;
    private static bool FixedEqual(string a, string b) => CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static ToolResponseDto<T> Response<T>(string tool, string requestId, string status, T? data, IEnumerable<string>? reasons = null, string? message = null) =>
        ToolResponseDto<T>.Create(tool, requestId, status, data, reasons?.ToArray(), new ToolMetaDto
        {
            SourceCompleteness = "partial", RegistryVersions = new() { Contract = AssessmentToolOptions.ContractVersion,
                Device = "project-online-device-minus-one", Schema = "assessment-summary-parsers-v1", Metric = AssessmentScaleCatalog.Version, State = "source_only_v1" }
        }, patientMessage: message, contractVersion: AssessmentToolOptions.ContractVersion);
}
