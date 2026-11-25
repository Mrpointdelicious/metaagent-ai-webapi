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
        "总分为源报告记录值；仅 AD8 可用选项分值复算校验，其余量表的计分方向、分界值与校正规则尚未核验。",
        "题目与选项原文不进入患者投影；逐题作答只在部分量表、部分报告格式中有记录。",
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
        if (patient == null || patient.Id != context.PatientId || patient.Status != 1 || patient.IsDeleted != 0 || context.Access == null && patient.BusinessId is not > 0)
            return Fail("ASSESSMENT_PATIENT_SCOPE_UNAVAILABLE", "无法确认当前患者数据范围。");
        var projected = new List<Projected>();
        foreach (var plan in source.Plans.Where(p => (context.Access != null ? context.Access.AllowsOwner(p.UserId) && context.Access.AllowsTenant(p.BusinessId) : p.UserId == patient.Id && p.BusinessId == patient.BusinessId) &&
            p.DeviceId == -1 && p.TemplateId is 0 or -1))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var item = Project(plan, source.Reports.Where(r => r.PlanId == plan.Id).OrderBy(r => r.Id).ToArray(), context.Access == null ? patient.BusinessId!.Value : plan.BusinessId ?? 0, context, operation == "result_analysis");
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
                ScoringStatus = AssessmentScaleCatalog.ProfileFor(pair.Key).ScoringStatus,
                RecordCount = rows.Count(p => p.Record.ScaleType == pair.Key),
                ReadableTotalCount = rows.Count(p => p.Record.ScaleType == pair.Key && p.Record.ReportedScore.HasValue)
            }).ToArray() : Array.Empty<AssessmentScaleCoverageDto>(),
            Records = records, PageNumber = operation == "patient_history" ? request.PageNumber : 0,
            HasNext = operation == "patient_history" && (long)request.PageNumber * request.PageSize < rows.Length,
            Comparability = operation == "longitudinal_analysis" ? "blocked_unverified_scoring_rules" : "not_requested",
            Interpretation = new()
            {
                Method = "source_assessment_reading_v2",
                Summary = contextRead
                    ? $"当前范围有 {rows.Length} 条线上记录，其中 {rows.Count(p => p.Record.ReportedScore.HasValue)} 条可读取源总分。"
                    : $"所选范围有 {records.Length} 条量表记录，其中 {records.Count(r => r.ReportedScore.HasValue)} 条可读取源总分。",
                Observations = operation is "patient_context" or "patient_history" ? Array.Empty<string>() : records.Select(r =>
                    $"{r.ScaleName ?? "待识别量表"}：" +
                    (r.ReportedScore.HasValue
                        ? "源报告总分 " + r.ReportedScore.Value.ToString(CultureInfo.InvariantCulture) +
                          (r.ScaleMaxScore.HasValue ? "（满分 " + r.ScaleMaxScore.Value.ToString(CultureInfo.InvariantCulture) + "）" : string.Empty)
                        : "缺少可核验总分，不能当作零分") +
                    (r.HasItemAnswers
                        ? $"，共 {r.QuestionCount} 题、已作答 {r.AnsweredItemCount} 题"
                        : r.QuestionCount > 0 ? $"，共 {r.QuestionCount} 题（该量表未记录逐题作答）" : string.Empty) + "。").ToArray(),
                Limitations = Limitations,
                NextChecks = new[]
                {
                    "先核对量表版本、计分方向与分界值，再开放同量表的数值变化与任何正常/异常判断。",
                    "逐题展示范围尚未确认：题目与选项原文仍不进入患者投影。",
                    "来源冲突、总分与复算不一致时，核对原始记录后再对外解释。"
                }
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

    private Projected? Project(RehabPlanEntity plan, RehabReportEntity[] reports, int institution, TrustedPatientContext context, bool includeItems)
    {
        var reasons = new List<string>();
        int? type = null;
        string? planName = null;
        try
        {
            var metadata = ReadJson(plan.Details);
            if (metadata["AccessType"] is JObject nested) metadata = nested;
            if (int.TryParse(metadata["type"]?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var code) &&
                AssessmentScaleCatalog.Names.ContainsKey(code)) type = code;
            planName = metadata["name"]?.Type == JTokenType.String ? metadata.Value<string>("name")?.Trim() : null;
        }
        catch (JsonException) { reasons.Add("ASSESSMENT_PLAN_SCHEMA_UNSUPPORTED"); }

        if (AssessmentScaleCatalog.IsOnlineGame(planName)) return null;

        if (!type.HasValue)
        {
            type = AssessmentScaleCatalog.ResolveType(planName);
            if (type.HasValue) reasons.Add("ASSESSMENT_SCALE_FROM_PLAN_NAME");
        }

        var planIdentity = type.HasValue && AssessmentScaleCatalog.Matches(type.Value, planName);
        if (!type.HasValue) reasons.Add("ASSESSMENT_SCALE_UNIDENTIFIED");
        else if (!string.IsNullOrEmpty(planName) && !planIdentity) reasons.Add("ASSESSMENT_PLAN_CATALOG_CONFLICT");
        else if (string.IsNullOrEmpty(planName)) reasons.Add("ASSESSMENT_PLAN_NAME_MISSING");

        var state = "no_result";
        var schema = "unrecognized";
        var identity = planIdentity ? "plan_catalog_only" : "unverified";
        decimal? score = null;
        decimal? recomputed = null;
        string? scoreField = null;
        var questionCount = 0;
        var answeredCount = 0;
        var hasItemAnswers = false;
        IReadOnlyList<AssessmentItemDto> items = Array.Empty<AssessmentItemDto>();

        var readable = reports.Where(r => !string.IsNullOrWhiteSpace(r.ReportDetails)).ToArray();
        if (readable.Length == 0) reasons.Add("ASSESSMENT_RESULT_MISSING");
        else
        {
            if (readable.Length != reports.Length) reasons.Add("ASSESSMENT_RESULT_PARTIALLY_MISSING");
            try
            {
                var parsed = readable.Select(r => ReadJson(r.ReportDetails!)).ToArray();
                if (parsed.Skip(1).Any(r => !JToken.DeepEquals(parsed[0], r)))
                {
                    state = "conflicting";
                    reasons.Add("ASSESSMENT_REPORT_CONFLICT");
                }
                else
                {
                    var report = ParseReport(parsed[0]);
                    questionCount = report.QuestionCount;
                    answeredCount = report.AnsweredCount;
                    hasItemAnswers = report.HasItemAnswers;
                    items = includeItems ? report.Items : Array.Empty<AssessmentItemDto>();
                    schema = report.SchemaFamily;

                    if (!type.HasValue)
                    {
                        type = AssessmentScaleCatalog.ResolveType(report.ScaleTitle);
                        if (type.HasValue) reasons.Add("ASSESSMENT_SCALE_FROM_REPORT_TITLE");
                    }

                    var reportIdentity = type.HasValue && AssessmentScaleCatalog.Matches(type.Value, report.ScaleTitle);
                    if (!type.HasValue || (!string.IsNullOrEmpty(planName) && !planIdentity) || !reportIdentity)
                    {
                        state = "identity_conflict";
                        reasons.Add("ASSESSMENT_REPORT_IDENTITY_UNVERIFIED");
                    }
                    else if (!report.Score.HasValue)
                    {
                        state = "invalid_score";
                        reasons.Add("ASSESSMENT_INVALID_SCORE");
                    }
                    else
                    {
                        identity = "catalog_and_report_match";
                        score = report.Score;
                        state = "reported_total_unverified";
                        scoreField = "dbreport.ReportDetails." + report.ScoreField;
                        reasons.Add("ASSESSMENT_SCORE_SEMANTICS_UNVERIFIED");
                        if (AssessmentScaleCatalog.ProfileFor(type.Value).ScoringStatus == "verified_option_sum" &&
                            report.RecomputedScore.HasValue)
                        {
                            recomputed = report.RecomputedScore;
                            reasons.Add(recomputed == score ? "ASSESSMENT_SCORE_RECOMPUTED" : "ASSESSMENT_SCORE_MISMATCH");
                        }
                    }
                }
            }
            catch (JsonException)
            {
                state = "unsupported_schema";
                reasons.Add("ASSESSMENT_REPORT_SCHEMA_UNSUPPORTED");
            }
        }

        var profile = type.HasValue ? AssessmentScaleCatalog.ProfileFor(type.Value) : ScaleProfile.Unknown;
        var uploadTime = reports.Select(r => Time(r.CreateTime)).Where(t => t.HasValue).OrderByDescending(t => t).FirstOrDefault();
        var record = new AssessmentRecordDto
        {
            AssessmentRef = "assessment_s_v1_" + Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(_options.ReferenceSigningKey), Encoding.UTF8.GetBytes($"assessment|project|{institution}|{plan.UserId}|{plan.Id}" + (context.Access == null ? string.Empty : "|scope:" + context.Access.ScopeFingerprint)))).ToLowerInvariant(),
            RecordTenant = plan.RecordTenant, OwnerUserRef = plan.OwnerUserRef,
            ScaleType = !string.IsNullOrEmpty(planName) && !planIdentity ? null : type,
            ScaleName = type.HasValue && (planIdentity || identity == "catalog_and_report_match") ? AssessmentScaleCatalog.Names[type.Value] : null,
            ScaleMaxScore = profile.MaxScore, ScoringStatus = profile.ScoringStatus,
            QuestionCount = questionCount, AnsweredItemCount = hasItemAnswers ? answeredCount : null, HasItemAnswers = hasItemAnswers,
            Items = items,
            IdentityStatus = identity, AssessmentOrigin = plan.DoctorId is not > 0 ? "unknown" : plan.DoctorId == plan.UserId ? "self" : "clinician_assigned",
            RecordedAt = uploadTime ?? Time(plan.BookingTime) ?? Time(plan.CreateTime),
            TimeMeaning = uploadTime.HasValue ? "report_uploaded_at" : Time(plan.BookingTime).HasValue ? "plan_scheduled_at" : "plan_created_at",
            ResultState = state, ReportedScore = score, RecomputedScore = recomputed, SourceScoreField = scoreField, SchemaFamily = schema,
            ReasonCodes = reasons, ReasonNotes = AssessmentReasonText.DescribeAll(reasons),
            Interpretation = AssessmentInterpretationRules.Interpret(type, score, profile.MaxScore)
        };
        return new(record, Hash(JsonConvert.SerializeObject(new { plan.Details, plan.BookingTime, plan.CreateTime, plan.DoctorId, reports = reports.Select(r => new { r.Id, r.ReportDetails, r.CreateTime, r.UpdateTime }) })));
    }

    /// <summary>
    /// Parses the three report shapes that exist in project.dbreport: the legacy
    /// name/scoreList summary, the current TableTitle/Options summary, and the MoCA-B
    /// variant that spells the option-score array OptionsScore.
    /// </summary>
    private static ParsedReport ParseReport(JObject json)
    {
        if (json["name"] is JValue legacyTitle && legacyTitle.Type == JTokenType.String)
            return ParseLegacySummary(json, legacyTitle.Value<string>()!.Trim());
        if (json["score"] != null && json["Score"] != null)
            throw new JsonReaderException("Ambiguous score field");
        return ParseModernSummary(json, json["score"] != null);
    }

    private static ParsedReport ParseLegacySummary(JObject json, string title)
    {
        if (string.IsNullOrEmpty(title)) throw new JsonReaderException("Legacy assessment summary without title");
        if (!TryDecimal(json["score"], out var score)) throw new JsonReaderException("Legacy assessment summary without numeric score");
        var questions = json["questionList"] as JArray;
        var optionScores = json["scoreList"] as JArray;
        var selections = json["selectList"] as JArray;
        if (questions is { Count: < 1 or > 500 }) throw new JsonReaderException("Unsupported question count");
        var questionCount = questions?.Count ?? selections?.Count ?? optionScores?.Count ?? 0;
        if (questionCount is < 1 or > 500) throw new JsonReaderException("Unsupported question count");
        return new ParsedReport("legacy_scale_summary_v1", score, "score", title, questionCount,
            CountAnswered(selections, questionCount), selections is { Count: > 0 }, Recompute(selections, optionScores, questionCount),
            BuildItems(selections, optionScores, questionCount));
    }

    private static ParsedReport ParseModernSummary(JObject json, bool lower)
    {
        var scoreToken = json[lower ? "score" : "Score"];
        var title = json[lower ? "tableTitle" : "TableTitle"];
        var questions = json[lower ? "title" : "Title"];
        if (!TryDecimal(scoreToken, out var score) || questions is not JArray array || array.Count is < 1 or > 500 ||
            title?.Type != JTokenType.String || string.IsNullOrWhiteSpace(title.Value<string>()))
            throw new JsonReaderException("Unsupported assessment summary");
        var schema = lower ? "lowercase_total_v1"
            : json["OptionsScore"] != null ? "options_score_total_v1"
            : json["OptionScore"] != null ? "option_score_total_v1"
            : "observed_total_v1";
        var optionScores = (json["OptionsScore"] as JArray) ?? (json["OptionScore"] as JArray) ?? (json["scoreList"] as JArray);
        var selections = (json["OptionProblem"] as JArray) ?? (json["selectList"] as JArray);
        return new ParsedReport(schema, score, lower ? "score" : "Score", title.Value<string>()!.Trim(), array.Count,
            CountAnswered(selections, array.Count), selections is { Count: > 0 }, Recompute(selections, optionScores, array.Count),
            BuildItems(selections, optionScores, array.Count));
    }

    private static bool TryDecimal(JToken? token, out decimal value)
    {
        value = 0m;
        if (token is null) return false;
        if (token.Type is JTokenType.Float or JTokenType.Integer) { value = token.Value<decimal>(); return true; }
        return token.Type == JTokenType.String &&
               decimal.TryParse(token.Value<string>(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>Counts entries that carry a real selection; -1 is the "not answered / padding" marker.</summary>
    private static int CountAnswered(JArray? selections, int questionCount)
    {
        if (selections is null || selections.Count == 0) return 0;
        var answered = 0;
        foreach (var item in selections.Take(questionCount))
            if (item.Type == JTokenType.Integer && item.Value<int>() >= 0) answered++;
        return answered;
    }

    /// <summary>
    /// Re-derives a total from "option score per item" plus "chosen option per item".
    /// Returns null whenever any item cannot be resolved, so a partial answer never
    /// produces a plausible-looking but wrong total.
    /// </summary>
    private static decimal? Recompute(JArray? selections, JArray? optionScores, int questionCount)
    {
        if (selections is null || optionScores is null) return null;
        if (selections.Count < questionCount || optionScores.Count < questionCount) return null;
        var total = 0m;
        for (var i = 0; i < questionCount; i++)
        {
            if (selections[i].Type != JTokenType.Integer) return null;
            var index = selections[i].Value<int>();
            if (index < 0) return null;
            var parts = OptionScoreParts(optionScores, i);
            if (parts is null || index >= parts.Count) return null;
            total += parts[index];
        }
        return total;
    }

    private const int MaxItemDetail = 100;

    /// <summary>
    /// Projects per-item facts for display. Returns an empty list when the source never
    /// recorded selections, so callers can distinguish "not recorded" from "all unanswered".
    /// </summary>
    private static IReadOnlyList<AssessmentItemDto> BuildItems(JArray? selections, JArray? optionScores, int questionCount)
    {
        if (selections is null || selections.Count == 0 || questionCount <= 0) return Array.Empty<AssessmentItemDto>();
        var count = Math.Min(questionCount, MaxItemDetail);
        var items = new List<AssessmentItemDto>(count);
        for (var i = 0; i < count; i++)
        {
            int? option = i < selections.Count && selections[i].Type == JTokenType.Integer
                ? selections[i].Value<int>()
                : null;
            if (option is < 0) option = null;
            var parts = OptionScoreParts(optionScores, i);
            items.Add(new AssessmentItemDto
            {
                Index = i + 1,
                OptionIndex = option,
                Score = option.HasValue && parts is not null && option.Value < parts.Count ? parts[option.Value] : null,
                MaxScore = parts is { Count: > 0 } ? parts.Max() : null,
                Answered = option.HasValue
            });
        }
        return items;
    }

    /// <summary>Splits one item's "1|0|0.5" option-score template into decimals.</summary>
    private static IReadOnlyList<decimal>? OptionScoreParts(JArray? optionScores, int index)
    {
        if (optionScores is null || index < 0 || index >= optionScores.Count) return null;
        var raw = optionScores[index];
        var text = raw.Type == JTokenType.String ? raw.Value<string>() : raw.ToString();
        if (string.IsNullOrWhiteSpace(text)) return null;
        var parts = new List<decimal>();
        foreach (var part in text!.Split('|'))
        {
            if (!decimal.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)) return null;
            parts.Add(value);
        }
        return parts;
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
            TenantSegments = AI_WebApi.Services.AITools.PatientContext.RecordTenantPresentation.Segments(data, context.Access),
            ArtifactRef = reference, ImageUrl = url, DisplayMarkdown = $"![线上量表记录]({url})", SourceSnapshot = data.SourceSnapshot,
            TemplateVersion = _options.ReportTemplateVersion, StorageMode = _artifacts.StorageMode
        }, analysis.ReasonCodes);
    }

    public static string BuildHtml(AssessmentToolDataDto data) =>
        AI_WebApi.Services.AITools.PatientContext.RecordTenantPresentation.AddToHtml(
            AssessmentReportHtmlBuilder.Build(data), data);

    private sealed record ParsedReport(
        string SchemaFamily, decimal? Score, string ScoreField, string ScaleTitle,
        int QuestionCount, int AnsweredCount, bool HasItemAnswers, decimal? RecomputedScore,
        IReadOnlyList<AssessmentItemDto> Items);

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
                Device = "project-online-device-minus-one", Schema = "assessment-summary-parsers-v2", Metric = AssessmentScaleCatalog.Version, State = "source_only_v1" }
        }, patientMessage: message, contractVersion: AssessmentToolOptions.ContractVersion);
}
