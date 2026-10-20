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

namespace AI_WebApi.Services.AITools.IReCog;

/// <summary>Observed assessment totals only. No item responses, scoring thresholds or invented instrument versions.</summary>
public sealed class IReCogToolService : IIReCogToolService
{
    private readonly IIReCogDataRepository _repository;
    private readonly IReCogToolOptions _options;
    private readonly IIReGoReportImageRenderer _renderer;
    private readonly IIReGoReportArtifactStore _artifacts;
    private readonly IReGoReportImageOptions _images;
    private static readonly string[] Boundaries =
    {
        "仅展示源报告记录的总分，未重新计算题目得分。",
        "量表身份、版本、分数范围与计分方向尚未核验，不判定认知障碍或疗效。",
        "不同量表不合并、不求平均；相同名称也不自动认定为相同版本。"
    };

    public IReCogToolService(IIReCogDataRepository repository, IOptions<IReCogToolOptions> options,
        IIReGoReportImageRenderer renderer, IIReGoReportArtifactStore artifacts, IOptions<IReGoToolOptions> images)
    { _repository = repository; _options = options.Value; _renderer = renderer; _artifacts = artifacts; _images = images.Value.ReportImage; }

    public async Task<ToolResponseDto<IReCogAnalysisDto>> ReadAsync(string operation, TrustedPatientContext context,
        IReCogRequestDto request, string requestId, CancellationToken cancellationToken)
    {
        var tool = "get_irecog_" + operation;
        ToolResponseDto<IReCogAnalysisDto> Fail(string code, string message) => Response<IReCogAnalysisDto>(tool, requestId, ToolStatuses.Unavailable, null, new[] { code }, message);
        if (operation is not ("patient_context" or "patient_history" or "session_analysis" or "longitudinal_analysis") || !Valid(request, operation))
            return Fail("INVALID_REQUEST", "查询参数不符合工具契约。");
        if (_options.SessionRefSigningKey.Length < 32) return Fail("IRECOG_SIGNING_KEY_NOT_CONFIGURED", "IReCog 引用签名未配置。");
        var source = await _repository.LoadAsync(context, cancellationToken);
        var patient = source.Patient;
        if (patient == null || patient.Id != context.PatientId || patient.Status != 1 || patient.IsDeleted != 0 || patient.BusinessId is not > 0)
            return Fail("PATIENT_SCOPE_UNAVAILABLE", "无法确认当前患者数据范围。");
        var rows = source.Plans.Where(p => p.UserId == patient.Id && p.BusinessId == patient.BusinessId)
            .Select(p => Project(p, source.Reports.Where(r => r.PlanId == p.Id).ToArray(), patient.BusinessId.Value))
            .OrderByDescending(p => p.RecordedAt).ThenBy(p => p.SessionRef, StringComparer.Ordinal).ToArray();
        IReadOnlyList<IReCogSessionDto> chosen;
        if (operation == "patient_context") chosen = Array.Empty<IReCogSessionDto>();
        else if (operation == "patient_history")
            chosen = rows.Skip((request.PageNumber - 1) * request.PageSize).Take(request.PageSize).ToArray();
        else if (operation == "session_analysis")
        {
            var row = request.Selector == "session_ref"
                ? rows.SingleOrDefault(r => FixedEqual(r.SessionRef, request.SessionRef!))
                : request.Selector == "latest_usable" ? rows.FirstOrDefault(r => r.ReportedScore.HasValue) : rows.FirstOrDefault();
            if (row == null) return Fail("SESSION_REF_NOT_FOUND", "没有匹配的 IReCog 记录。");
            chosen = new[] { row };
        }
        else
        {
            chosen = (request.SelectionMode == "date_range"
                ? rows.Where(r => r.RecordedAt >= request.StartDate && r.RecordedAt <= request.EndDate)
                : rows.Take(request.ReportCount)).OrderBy(r => r.RecordedAt).ThenBy(r => r.SessionRef, StringComparer.Ordinal).ToArray();
            if (chosen.Count is < 2 or > 20) return Fail("IRECOG_INVALID_WINDOW", "请选择 2 至 20 条记录；日期范围不自动截断。");
        }
        var data = new IReCogAnalysisDto
        {
            Counts = new Dictionary<string, int> { ["record_count"] = rows.Length, ["readable_total_count"] = rows.Count(r => r.ReportedScore.HasValue), ["missing_result_count"] = rows.Count(r => r.ResultState == "no_result") },
            Sessions = chosen, PageNumber = operation == "patient_history" ? request.PageNumber : 0,
            TotalItems = rows.Length, HasNext = operation == "patient_history" && (long)request.PageNumber * request.PageSize < rows.Length,
            Comparability = operation == "longitudinal_analysis" ? "blocked_unverified_instrument_version" : "not_requested",
            Interpretation = new()
            {
                Method = "source_assessment_summary_v1",
                Summary = operation == "patient_context" ? $"当前患者有 {rows.Length} 条 IReCog 关联记录，其中 {rows.Count(r => r.ReportedScore.HasValue)} 条可展示源报告总分。" : $"所选范围有 {chosen.Count} 条 IReCog 关联记录，其中 {chosen.Count(r => r.ReportedScore.HasValue)} 条可展示源报告总分。",
                Observations = operation is "patient_context" or "patient_history" ? Array.Empty<string>() : chosen.Select(r =>
                    r.ReportedScore.HasValue ? $"{r.RecordedAt:yyyy-MM-dd}：源报告总分 {r.ReportedScore.Value.ToString(CultureInfo.InvariantCulture)}；尚无经核验的临床解释。" : "该记录缺少可核验总分，不能按零分处理。").ToArray(),
                Limitations = Boundaries,
                NextChecks = new[] { "核对量表名称、版本、计分方向与适用人群后，再开放同版本的分数趋势。" }
            }
        };
        data.SourceSnapshot = Hash(JsonConvert.SerializeObject(new { domain = "irecog", patient.Id, patient.BusinessId, data.Sessions, data.Comparability, data.Interpretation }));
        var reasons = chosen.SelectMany(s => s.ReasonCodes).Distinct().ToList();
        if (operation == "longitudinal_analysis") reasons.Add("IRECOG_INSTRUMENT_VERSION_UNVERIFIED");
        return Response(tool, requestId, reasons.Count > 0 ? ToolStatuses.Partial : ToolStatuses.Success, data, reasons);
    }

    private IReCogSessionDto Project(RehabPlanEntity plan, RehabReportEntity[] reports, int institution)
    {
        var reason = new List<string>();
        var state = "no_result";
        string? title = null;
        decimal? score = null;
        var schema = "unrecognized";
        var distinct = reports.Select(r => r.ReportDetails).Distinct(StringComparer.Ordinal).ToArray();
        if (distinct.Length > 1) { reason.Add("IRECOG_REPORT_CONFLICT"); state = "conflicting"; }
        else if (distinct.Length == 1 && !string.IsNullOrWhiteSpace(distinct[0]))
        {
            try
            {
                var raw = distinct[0]!;
                if (raw.Length > 524288) throw new JsonReaderException("Oversize source");
                using var reader = new JsonTextReader(new StringReader(raw)) { MaxDepth = 32, FloatParseHandling = FloatParseHandling.Decimal };
                var json = JObject.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
                if (json["Title"] is not JArray questions || questions.Count == 0 || questions.Count > 500 ||
                    json["TableTitle"]?.Type != JTokenType.String || json["Score"]?.Type is not (JTokenType.Integer or JTokenType.Float or JTokenType.String))
                    throw new JsonReaderException("Unsupported assessment schema");
                title = json.Value<string>("TableTitle")?.Trim();
                if (string.IsNullOrEmpty(title) || title.Length > 120) throw new JsonReaderException("Invalid title");
                schema = "observed_assessment_total_v1";
                if (decimal.TryParse(json["Score"]!.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                { score = number; state = "reported_total_unverified"; reason.Add("IRECOG_SCORE_SEMANTICS_UNVERIFIED"); }
                else { state = "invalid_score"; reason.Add("IRECOG_INVALID_SCORE"); }
            }
            catch (JsonException) { title = null; state = "unsupported_schema"; reason.Add("IRECOG_SCHEMA_UNSUPPORTED"); }
        }
        else reason.Add("IRECOG_RESULT_MISSING");
        return new()
        {
            SessionRef = "irecog_s_v1_" + Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(_options.SessionRefSigningKey),
                Encoding.UTF8.GetBytes($"irecog|project|{institution}|{plan.UserId}|{plan.Id}"))).ToLowerInvariant(),
            RecordedAt = Time(reports.OrderByDescending(r => r.CreateTime).FirstOrDefault()?.CreateTime) ?? Time(plan.StartTime) ?? Time(plan.BookingTime) ?? Time(plan.CreateTime),
            SourceCompletionFlag = plan.IsComplete, ResultState = state, ReportedScore = score, InstrumentTitle = title,
            InstrumentRef = title == null ? null : "observed_instrument_" + Hash($"{plan.TemplateId}|{title}"),
            SchemaFamily = schema, ReasonCodes = reason
        };
    }

    public async Task<ToolResponseDto<IReTourSingleSessionReportArtifactDto>> ReportAsync(bool longitudinal,
        TrustedPatientContext context, IReCogRequestDto request, string baseUrl, string requestId, CancellationToken cancellationToken)
    {
        var tool = longitudinal ? "generate_irecog_longitudinal_report" : "generate_irecog_single_session_report";
        var analysis = await ReadAsync(longitudinal ? "longitudinal_analysis" : "session_analysis", context, request, requestId, cancellationToken);
        if (analysis.Data == null || analysis.Data.Sessions.Count == 0)
            return Response<IReTourSingleSessionReportArtifactDto>(tool, requestId, ToolStatuses.Unavailable, null, analysis.ReasonCodes);
        var data = analysis.Data;
        if (request.ExpectedSourceSnapshot != null && request.ExpectedSourceSnapshot != data.SourceSnapshot)
            return Response<IReTourSingleSessionReportArtifactDto>(tool, requestId, ToolStatuses.Unavailable, null, new[] { "IRECOG_SOURCE_CHANGED" });
        if (!_artifacts.IsConfigured) return Response<IReTourSingleSessionReportArtifactDto>(tool, requestId, ToolStatuses.Unavailable, null, new[] { "IRECOG_REPORT_STORE_NOT_CONFIGURED" });
        var reference = _artifacts.CreateArtifactRef(context.PatientRef, "irecog:" + string.Join("|", data.Sessions.Select(s => s.SessionRef)), data.SourceSnapshot, _options.ReportTemplateVersion);
        if (!_artifacts.TryGet(reference, out _))
        {
            try { _artifacts.Store(reference, await _renderer.RenderPngAsync(BuildHtml(data), cancellationToken)); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception) { return Response<IReTourSingleSessionReportArtifactDto>(tool, requestId, ToolStatuses.Unavailable, null, new[] { "IRECOG_RENDERING_UNAVAILABLE" }); }
        }
        var origin = string.IsNullOrWhiteSpace(_images.PublicBaseUrl) ? baseUrl : _images.PublicBaseUrl;
        var url = origin.TrimEnd('/') + "/api/ai/patients/report-artifacts/" + Uri.EscapeDataString(reference) + "/content";
        return Response(tool, requestId, ToolStatuses.Partial, new IReTourSingleSessionReportArtifactDto
        {
            ArtifactRef = reference, ImageUrl = url, DisplayMarkdown = $"![IReCog测评记录汇总]({url})",
            SourceSnapshot = data.SourceSnapshot, TemplateVersion = _options.ReportTemplateVersion, StorageMode = _artifacts.StorageMode
        }, analysis.ReasonCodes);
    }

    public static string BuildHtml(IReCogAnalysisDto data)
    {
        static string E(string? s) => WebUtility.HtmlEncode(s ?? "未记录");
        var html = new StringBuilder("<!doctype html><html lang=\"zh-CN\"><meta charset=\"utf-8\"><title>IReCog 测评记录</title><style>body{font:17px/1.7 sans-serif;color:#183344;background:#edf3f5;margin:0;padding:40px}main{max-width:1100px;margin:auto;background:white;padding:40px;border-radius:20px}header{border-bottom:4px solid #137d86}table{border-collapse:collapse;width:100%;margin:25px 0}td,th{padding:14px;text-align:left;border-bottom:1px solid #d9e4e8;overflow-wrap:anywhere}small{overflow-wrap:anywhere;color:#607680}.note{background:#f2f6f8;padding:20px;border-radius:12px}</style><main><header><small>IRECOG · ASSESSMENT RECORDS</small><h1>测评记录汇总</h1></header><p>");
        html.Append(E(data.Interpretation.Summary)).Append("</p><table><tr><th>记录时间</th><th>源量表名称</th><th>源报告总分</th><th>数据状态</th></tr>");
        foreach (var s in data.Sessions)
            html.Append("<tr><td>").Append(E(s.RecordedAt?.ToString("yyyy-MM-dd HH:mm"))).Append("</td><td>").Append(E(s.InstrumentTitle))
                .Append("</td><td>").Append(E(s.ReportedScore?.ToString(CultureInfo.InvariantCulture))).Append("</td><td>").Append(E(s.ResultState switch
                {
                    "reported_total_unverified" => "有源总分，语义待核验", "no_result" => "缺少结果",
                    "conflicting" => "源报告冲突", "invalid_score" => "总分无效", _ => "结果结构尚未支持"
                })).Append("</td></tr>");
        html.Append("</table><section class=\"note\"><h2>解读范围</h2><ul>");
        foreach (var note in data.Interpretation.Limitations.Concat(data.Interpretation.NextChecks)) html.Append("<li>").Append(E(note)).Append("</li>");
        return html.Append("</ul></section><p><small>数据快照 ").Append(E(data.SourceSnapshot)).Append("</small></p></main></html>").ToString();
    }

    private static bool Valid(IReCogRequestDto r, string operation) => r.PageNumber is >= 1 and <= 100000 && r.PageSize is >= 1 and <= 50 &&
        r.Selector is "latest_record" or "latest_usable" or "session_ref" &&
        (r.Selector != "session_ref" || r.SessionRef is { Length: > 0 and <= 100 }) &&
        (operation != "longitudinal_analysis" || (r.SelectionMode == "latest_count" && r.ReportCount is >= 2 and <= 20 && r.StartDate == null && r.EndDate == null ||
            r.SelectionMode == "date_range" && r.StartDate.HasValue && r.EndDate.HasValue && r.StartDate <= r.EndDate && (r.EndDate.Value - r.StartDate.Value).TotalDays <= 180));
    private static bool FixedEqual(string a, string b) => CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));
    private static DateTimeOffset? Time(DateTime? value) => value is { Year: > 1900 } v
        ? new DateTimeOffset(DateTime.SpecifyKind(v, DateTimeKind.Unspecified), TimeSpan.FromHours(8)) : null;
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static ToolResponseDto<T> Response<T>(string tool, string requestId, string status, T? data,
        IEnumerable<string>? reasons = null, string? message = null) => ToolResponseDto<T>.Create(tool, requestId, status,
            data, reasons?.ToArray(), new ToolMetaDto { SourceCompleteness = "partial", RegistryVersions = new() { Contract = IReCogToolOptions.ContractVersion, Device = "project-device-type-5", Schema = "observed_assessment_total_v1", Metric = "source_total_unverified", State = "source_flags_only" } },
            patientMessage: message, contractVersion: IReCogToolOptions.ContractVersion);
}
