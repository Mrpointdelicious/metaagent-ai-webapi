using System.Globalization;
using System.Net;
using System.Text;
using AI_WebApi.IServices.AITools;
using AI_WebApi.Model.DTOs.AITools;

namespace AI_WebApi.Services.AITools.HospitalOperations;

public sealed class HospitalOperationsReportHtmlBuilder :
    IHospitalOperationsReportHtmlBuilder
{
    public string Build(HospitalOperationsResultDto result)
    {
        var title = result.Scope switch
        {
            "platform" => "平台运营分析报告",
            "comparison" => "医院运营对比报告",
            _ => $"{result.Hospitals.First().Hospital.HospitalName}运营分析报告"
        };
        var html = new StringBuilder();
        html.Append(@"
<!doctype html><html lang='zh-CN'><head><meta charset='utf-8'>
<meta name='viewport' content='width=device-width,initial-scale=1'>
<title>医院运营分析报告</title><style>
*{box-sizing:border-box}body{margin:0;background:#eaf3f6;color:#12384a;font-family:'Noto Sans CJK SC','Microsoft YaHei',Arial,sans-serif} .page{width:1120px;margin:28px auto;background:#fff;border-radius:26px;overflow:hidden;box-shadow:0 22px 60px rgba(20,73,91,.16)}
.hero{padding:42px 48px 34px;color:#fff;background:linear-gradient(115deg,#17617d 0%,#278ca0 55%,#31ad9c 100%);position:relative}.hero:after{content:'';position:absolute;width:300px;height:300px;border:40px solid rgba(255,255,255,.05);border-radius:50%;right:-90px;top:-160px}.eyebrow{font-size:12px;letter-spacing:2px;font-weight:800;opacity:.86}.hero h1{margin:12px 0 5px;font-size:32px}.hero p{margin:0;opacity:.9}.meta{display:grid;grid-template-columns:1.5fr 1fr 1fr;gap:12px;margin-top:27px}.meta div{padding:12px 15px;border:1px solid rgba(255,255,255,.22);background:rgba(255,255,255,.10);border-radius:10px}.meta small{display:block;opacity:.72;margin-bottom:4px}.content{padding:18px 45px 42px}.section{padding:27px 0;border-bottom:1px solid #d9e7ec}.section:last-child{border-bottom:0}.kicker{color:#2092a1;font-size:11px;letter-spacing:1.6px;font-weight:900}.section h2{margin:5px 0 17px;font-size:23px}.cards{display:grid;grid-template-columns:repeat(3,1fr);gap:12px}.card{border:1px solid #d5e4e9;background:#fbfdfe;border-radius:14px;padding:16px}.card label{display:block;color:#64808c;font-size:12px}.value{font-size:27px;font-weight:850;margin-top:8px}.unit{font-size:12px;font-weight:600;margin-left:3px}.hospital{margin:12px 0 20px;padding:15px 18px;background:#edf7f7;border-left:4px solid #24a395;border-radius:9px;font-weight:800}.split{display:grid;grid-template-columns:1fr 1fr;gap:14px}.panel{border:1px solid #d5e4e9;border-radius:14px;padding:18px}.panel h3{margin:0 0 14px;font-size:16px}.row{display:flex;align-items:center;justify-content:space-between;padding:9px 0;border-bottom:1px solid #eef3f5}.row:last-child{border-bottom:0}.row b{font-size:18px}.metric{display:flex;gap:10px;flex-wrap:wrap;margin-top:13px}.pill{background:#edf7f5;color:#127d74;padding:7px 10px;border-radius:999px;font-size:12px}.analysis{display:grid;grid-template-columns:repeat(2,1fr);gap:12px}.analysis article{border:1px solid #d7e6eb;background:#fbfdfe;border-radius:12px;padding:15px}.analysis h3{font-size:15px;margin:0 0 8px}.analysis p{color:#476570;font-size:13px;line-height:1.72;margin:0}.compare-wrap{overflow:hidden;border:1px solid #d5e4e9;border-radius:14px}.compare{width:100%;border-collapse:collapse;font-size:13px}.compare th{padding:12px 10px;text-align:right;color:#fff;background:#1e778c}.compare th:first-child{text-align:left}.compare td{padding:11px 10px;text-align:right;border-bottom:1px solid #e5eef1}.compare td:first-child{text-align:left}.compare tr:last-child td{border-bottom:0}.compare .scope{display:block;margin-top:3px;color:#80939b;font-size:10px}.compare .delta{font-weight:800;color:#167d78}.note{margin-top:16px;color:#68808a;font-size:11px;line-height:1.7;background:#f0f5f7;border-radius:8px;padding:11px 13px}.footer{padding:18px 45px;color:#78909a;font-size:11px;display:flex;justify-content:space-between;background:#fbfdfe}@media(max-width:900px){.page{width:100%;margin:0;border-radius:0}.meta,.cards,.split,.analysis{grid-template-columns:1fr}.content,.hero{padding-left:24px;padding-right:24px}}
</style></head><body><main class='page'>
");
        html.Append("<header class=\"hero\"><div class=\"eyebrow\">METAVERSE 3.0 · HOSPITAL OPERATIONS</div><h1>")
            .Append(E(title))
            .Append("</h1><p>医院目录与运营数据查询结果</p><div class=\"meta\"><div><small>查询对象</small>")
            .Append(E(string.Join("、", result.Hospitals.Select(
                x => x.Hospital.HospitalName))))
            .Append("</div><div><small>统计区间</small>")
            .Append(E($"{result.Period.StartDate} 至 {result.Period.EndDate}"))
            .Append("</div><div><small>报表范围</small>")
            .Append(E(SectionLabel(result.Section)))
            .Append("</div></div></header><div class=\"content\">");

        if (result.Comparison != null)
        {
            var comparison = result.Comparison;
            SectionStart(html, "PAIRWISE COMPARISON", "两院指标对照");
            html.Append("<div class=\"compare-wrap\"><table class=\"compare\"><thead><tr><th>指标</th><th>")
                .Append(E(comparison.LeftHospital.HospitalName))
                .Append("</th><th>")
                .Append(E(comparison.RightHospital.HospitalName))
                .Append("</th><th>差值（右－左）</th></tr></thead><tbody>");
            foreach (var metric in comparison.Metrics)
            {
                html.Append("<tr><td>")
                    .Append(E(metric.Label))
                    .Append("<span class=\"scope\">")
                    .Append(E(SectionLabel(metric.Section)))
                    .Append(" · ")
                    .Append(E(TimeScopeLabel(metric.TimeScope)))
                    .Append("</span></td><td>")
                    .Append(E(metric.LeftDisplayValue))
                    .Append("</td><td>")
                    .Append(E(metric.RightDisplayValue))
                    .Append("</td><td class=\"delta\">")
                    .Append(E(metric.DifferenceDisplayValue))
                    .Append("</td></tr>");
            }
            html.Append("</tbody></table></div>");
            if (comparison.Notes.Count > 0)
            {
                html.Append("<div class=\"note\">")
                    .Append(E(string.Join(" ", comparison.Notes)))
                    .Append("</div>");
            }
            SectionEnd(html);
        }

        if (result.Section is "all" or "overview")
        {
            SectionStart(html, "01 · OVERVIEW", "运营总览");
            foreach (var snapshot in result.Hospitals.Where(x => x.Overview != null))
            {
                HospitalLabel(html, snapshot);
                var value = snapshot.Overview!;
                Cards(html, new (string Label, decimal Value, string Unit)[]
                {
                    ("注册患者", value.PatientCount, "人"),
                    ("医生", value.DoctorCount, "人"),
                    ("当前活跃患者", value.DailyActiveCount, "人")
                });
                Metrics(html, snapshot);
            }
            SectionEnd(html);
        }

        if (result.Section is "all" or "consultation")
        {
            SectionStart(html, "02 · CONSULTATION", "问诊情况");
            foreach (var snapshot in result.Hospitals.Where(x => x.Consultation != null))
            {
                HospitalLabel(html, snapshot);
                var value = snapshot.Consultation!;
                var average = value.ConsultationPeople == 0
                    ? 0
                    : (decimal)value.ConsultationTimes / value.ConsultationPeople;
                Cards(html, new (string Label, decimal Value, string Unit)[]
                {
                    ("问诊人数", value.ConsultationPeople, "人"),
                    ("问诊次数", value.ConsultationTimes, "次"),
                    ("人均问诊", average, "次/人")
                });
            }
            SectionEnd(html);
        }

        if (result.Section is "all" or "rooms")
        {
            SectionStart(html, "03 · VIRTUAL ROOMS", "虚拟诊室");
            foreach (var snapshot in result.Hospitals.Where(x => x.Rooms != null))
            {
                HospitalLabel(html, snapshot);
                var value = snapshot.Rooms!;
                Cards(html, new (string Label, decimal Value, string Unit)[]
                {
                    ("问诊室", value.ConsultationRooms, "间"),
                    ("评估室", value.AssessmentRooms, "间"),
                    ("训练室", value.TrainingRooms, "间")
                });
            }
            SectionEnd(html);
        }

        if (result.Section is "all" or "assessment_training")
        {
            SectionStart(html, "04 · ASSESSMENT & TRAINING", "评估与训练");
            foreach (var snapshot in result.Hospitals.Where(
                         x => x.AssessmentTraining != null))
            {
                HospitalLabel(html, snapshot);
                var value = snapshot.AssessmentTraining!;
                html.Append("<div class=\"split\"><div class=\"panel\"><h3>评估</h3>");
                Row(html, "评估总次数", value.AssessmentCount, "次");
                Row(html, "量表评估", value.ScaleAssessmentCount, "次");
                Row(html, "范式评估", value.ParadigmAssessmentCount, "次");
                html.Append("</div><div class=\"panel\"><h3>训练</h3>");
                Row(html, "离线训练", value.OfflineTrainingCount, "次");
                Row(html, "设备训练", value.DeviceTrainingCount, "次");
                Row(html, "通道训练", value.CorridorTrainingCount, "次");
                html.Append("</div></div>");
            }
            SectionEnd(html);
        }

        if (result.Section is "all" or "records")
        {
            SectionStart(html, "05 · SERVICE & RECORDS", "问诊类型与业务资料");
            foreach (var snapshot in result.Hospitals.Where(x => x.Records != null))
            {
                HospitalLabel(html, snapshot);
                var value = snapshot.Records!;
                html.Append("<div class=\"split\"><div class=\"panel\"><h3>问诊类型</h3>");
                Row(html, "真人问诊", value.RealConsultationCount, "次");
                Row(html, "AI问诊", value.AiConsultationCount, "次");
                Row(html, "远程会诊", value.RemoteConsultationCount, "次");
                html.Append("</div><div class=\"panel\"><h3>业务资料</h3>");
                Row(html, "就诊记录", value.MedicalRecordCount, "条");
                Row(html, "处方模板", value.PrescriptionTemplateCount, "条");
                Row(html, "报告", value.ReportCount, "份");
                html.Append("</div></div>");
            }
            SectionEnd(html);
        }

        if (result.Analysis.Count > 0)
        {
            SectionStart(html, "06 · AI ANALYSIS", "AI 智能分析");
            html.Append("<div class=\"analysis\">");
            foreach (var item in result.Analysis)
            {
                html.Append("<article><h3>")
                    .Append(E(item.Title))
                    .Append("</h3><p>")
                    .Append(E(item.Text))
                    .Append("</p></article>");
            }
            html.Append("</div>");
            if (result.DataNotes.Count > 0)
            {
                html.Append("<div class=\"note\">")
                    .Append(E(string.Join(" ", result.DataNotes)))
                    .Append("</div>");
            }
            SectionEnd(html);
        }

        html.Append("</div><footer class=\"footer\"><span>医院运营数据分析报告</span><span>稳定报表引用 · 可重复访问</span></footer></main></body></html>");
        return html.ToString();
    }

    private static void SectionStart(StringBuilder html, string kicker, string title) =>
        html.Append("<section class=\"section\"><div class=\"kicker\">")
            .Append(E(kicker))
            .Append("</div><h2>")
            .Append(E(title))
            .Append("</h2>");

    private static void SectionEnd(StringBuilder html) => html.Append("</section>");

    private static void HospitalLabel(
        StringBuilder html,
        HospitalOperationsSnapshotDto snapshot) =>
        html.Append("<div class=\"hospital\">")
            .Append(E(snapshot.Hospital.HospitalName))
            .Append("</div>");

    private static void Cards(
        StringBuilder html,
        IEnumerable<(string Label, decimal Value, string Unit)> values)
    {
        html.Append("<div class=\"cards\">");
        foreach (var value in values)
        {
            html.Append("<div class=\"card\"><label>")
                .Append(E(value.Label))
                .Append("</label><div class=\"value\">")
                .Append(value.Value.ToString("0.##", CultureInfo.InvariantCulture))
                .Append("<span class=\"unit\">")
                .Append(E(value.Unit))
                .Append("</span></div></div>");
        }
        html.Append("</div>");
    }

    private static void Metrics(
        StringBuilder html,
        HospitalOperationsSnapshotDto snapshot)
    {
        if (snapshot.DerivedMetrics.Count == 0)
        {
            return;
        }
        html.Append("<div class=\"metric\">");
        foreach (var metric in snapshot.DerivedMetrics)
        {
            html.Append("<span class=\"pill\">")
                .Append(E(metric.Label))
                .Append(" ")
                .Append(E(metric.DisplayValue))
                .Append("</span>");
        }
        html.Append("</div>");
    }

    private static void Row(
        StringBuilder html,
        string label,
        decimal value,
        string unit) =>
        html.Append("<div class=\"row\"><span>")
            .Append(E(label))
            .Append("</span><b>")
            .Append(value.ToString("0.##", CultureInfo.InvariantCulture))
            .Append(" <small>")
            .Append(E(unit))
            .Append("</small></b></div>");

    private static string SectionLabel(string section) => section switch
    {
        "overview" => "运营总览",
        "consultation" => "问诊情况",
        "rooms" => "虚拟诊室",
        "assessment_training" => "评估与训练",
        "records" => "业务资料",
        "analysis" => "综合分析",
        _ => "完整报告"
    };

    private static string TimeScopeLabel(string value) => value switch
    {
        "query_window" => "查询日期区间",
        "current_snapshot" => "当前快照",
        "upstream_snapshot" => "上游快照",
        _ => "接口口径"
    };

    private static string E(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
}
