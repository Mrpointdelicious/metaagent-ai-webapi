using System.Collections;
using System.Net;
using System.Reflection;
using System.Text;
using System.Security.Cryptography;
using AI_WebApi.Model.Domain.IReGo;
using AI_WebApi.Model.DTOs.AITools;
using Newtonsoft.Json.Linq;

namespace AI_WebApi.Services.AITools.PatientContext;

public static class RecordTenantPresentation
{
    public static string SourceSnapshot(object data, PatientAccessContext? access)
    {
        var json = JObject.FromObject(data); json.Remove("source_snapshot");
        var payload = (access?.ScopeFingerprint ?? "internal") + "|" + json.ToString(Newtonsoft.Json.Formatting.None);
        return "source_v2_" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }

    public static IReadOnlyList<PatientTenantSegmentDto> Segments(object? data, PatientAccessContext? access)
    {
        if (data == null || access == null) return Array.Empty<PatientTenantSegmentDto>();
        var json = data as JObject ?? JObject.FromObject(data);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var records = new List<(string Reference, DateTimeOffset? Time, RecordTenantDto Tenant)>();
        foreach (var item in json.DescendantsAndSelf().OfType<JObject>().Where(x => x["record_tenant"] is JObject))
        {
            var reference = ReferenceOf(item);
            if (reference == null && item["plan_id"]?.Type == JTokenType.Integer)
                reference = access.Reference("record", "project.dbrehaplan:" + item["plan_id"]);
            if (reference == null || !seen.Add(access.RecordKey(reference))) continue;
            var time = TimeOf(item);
            DateTimeOffset? instant = DateTimeOffset.TryParse(time?.ToString(), System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind, out var parsed) ? parsed : null;
            records.Add((reference, instant, item["record_tenant"]!.ToObject<RecordTenantDto>()!));
        }
        var result = new List<PatientTenantSegmentDto>();
        var ordered = records.OrderBy(r => r.Time ?? DateTimeOffset.MaxValue).ThenBy(r => r.Reference, StringComparer.Ordinal).ToArray();
        for (var start = 0; start < ordered.Length;)
        {
            var end = start + 1;
            while (end < ordered.Length && ordered[end].Tenant.TenantRef == ordered[start].Tenant.TenantRef &&
                ordered[end].Tenant.AttributionStatus == ordered[start].Tenant.AttributionStatus) end++;
            var slice = ordered[start..end];
            result.Add(new() { RecordTenant = slice[0].Tenant, RecordRefs = slice.Select(r => r.Reference).ToArray(),
                FirstRecordedAt = slice.Where(r => r.Time.HasValue).Select(r => r.Time).FirstOrDefault(),
                LastRecordedAt = slice.Where(r => r.Time.HasValue).Select(r => r.Time).LastOrDefault() });
            start = end;
        }
        return result;
    }

    /// <summary>Project verified source annotations into shared DTOs; never infer an institution from the current login.</summary>
    public static void Annotate(object? data, PatientAccessContext? access)
    {
        if (data == null || access == null) return;
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        void Visit(object? value, int depth)
        {
            if (value == null || value is string || value.GetType().IsValueType || depth > 32 || !seen.Add(value)) return;
            if (value is IEnumerable items) { foreach (var item in items) Visit(item, depth + 1); return; }
            var type = value.GetType();
            if (type.Namespace != "AI_WebApi.Model.DTOs.AITools") return;
            var tenantProperty = type.GetProperty("RecordTenant");
            if (tenantProperty != null && tenantProperty.GetValue(value) == null)
                foreach (var name in new[] { "SessionRef", "PlanRef", "AssessmentRef" })
                    if (type.GetProperty(name)?.GetValue(value) is string reference)
                    {
                        var attribution = access.Attribution(reference);
                        if (attribution.Tenant == null) continue;
                        tenantProperty.SetValue(value, attribution.Tenant);
                        type.GetProperty("OwnerUserRef")?.SetValue(value, attribution.Owner);
                        break;
                    }
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.GetIndexParameters().Length == 0))
                Visit(property.GetValue(value), depth + 1);
        }
        Visit(data, 0);
    }

    public static string AddToHtml(string html, object data)
    {
        var json = JObject.FromObject(data);
        var singleSession = json["session"] is JObject session && ReferenceOf(session) != null;
        var records = json.DescendantsAndSelf().OfType<JObject>()
            .Where(o => o["record_tenant"] is JObject && !(singleSession && o["plan_ref"] != null))
            .Select(o => new { Record = ReferenceOf(o), Time = TimeOf(o), Tenant = (JObject)o["record_tenant"]! })
            .Where(x => x.Record != null).GroupBy(x => x.Record, StringComparer.Ordinal).Select(g => g.First()).ToArray();
        if (records.Length == 0) return html;
        static string E(string? text) => WebUtility.HtmlEncode(text ?? "未记录");
        var section = new StringBuilder("<section style=\"margin:20px 0;padding:18px;border:1px solid #cedce0;border-radius:12px;break-inside:avoid\"><h2>康复记录所属机构</h2><table style=\"width:100%;border-collapse:collapse\"><thead><tr><th>记录</th><th>记录时间</th><th>所属机构</th></tr></thead><tbody>");
        for (var i = 0; i < records.Length; i++)
            section.Append("<tr><td>").Append(i + 1).Append("</td><td>").Append(E(records[i].Time?.ToString()))
                .Append("</td><td>").Append(E(records[i].Tenant.Value<string>("display_name"))).Append("</td></tr>");
        section.Append("</tbody></table><p style=\"font-size:13px\">机构按每条历史记录的归属标注；记录时间范围不表示连续住院或治疗期间。</p></section>");
        var heading = html.IndexOf("</h1>", StringComparison.OrdinalIgnoreCase);
        return heading >= 0 ? html.Insert(heading + 5, section.ToString()) : section + html;
    }

    private static string? ReferenceOf(JObject item) => new[] { "record_ref", "session_ref", "assessment_ref", "plan_ref" }
        .Select(name => item.Value<string>(name)).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static JToken? TimeOf(JObject item)
    {
        var time = new[] { "session_time", "recorded_at", "scheduled_at", "created_at", "report_recorded_at" }
            .Select(name => item[name]).FirstOrDefault(value => value != null && value.Type != JTokenType.Null);
        if (time == null && item.Parent?.Parent is JObject parent && parent["time"] is JObject times)
            time = new[] { "report_recorded_at", "execution_started_at", "source_record_created_at" }
                .Select(name => times[name]).FirstOrDefault(value => value != null && value.Type != JTokenType.Null);
        return time;
    }
}
