using System.Net;
using System.Text;
using AI_WebApi.Model.DTOs.AITools;

namespace AI_WebApi.Services.AITools.IReTour;

internal static class IReTourReadingHtml
{
    public static void Append(StringBuilder html, IReTourReadingDto reading, string snapshot)
    {
        if (string.IsNullOrWhiteSpace(reading.Summary)) return;
        html.Append("<section style=\"margin:24px 0;padding:24px;background:#f3f7fa;border-radius:16px;overflow-wrap:anywhere\"><h2>数据解读与待核对事项</h2><p>")
            .Append(WebUtility.HtmlEncode(reading.Summary)).Append("</p><ul>");
        foreach (var text in reading.Observations.Concat(reading.Limitations).Concat(reading.NextChecks))
            html.Append("<li>").Append(WebUtility.HtmlEncode(text)).Append("</li>");
        html.Append("</ul><small>后端规则描述 · 数据快照 ").Append(WebUtility.HtmlEncode(snapshot)).Append("</small></section>");
    }
}
