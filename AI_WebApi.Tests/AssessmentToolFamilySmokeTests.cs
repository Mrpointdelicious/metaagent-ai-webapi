using AI_WebApi.IServices.AITools;
using AI_WebApi.Model.AITools;
using AI_WebApi.Model.Domain.IReGo;
using AI_WebApi.Model.DTOs.AITools;
using AI_WebApi.Model.Entities;
using AI_WebApi.Repository.AITools;
using AI_WebApi.Services.AITools.Assessment;
using AI_WebApi.Services.AITools.IReGo;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace AI_WebApi.Tests;

public sealed class AssessmentToolFamilySmokeTests
{
    [Fact]
    public async Task Online_assessment_family_smoke()
    {
        var fixture = new Fixture();
        var renderer = new Renderer();
        var imageOptions = Options.Create(new IReGoToolOptions { ReportImage = new() { SigningKey = new string('r', 40) } });
        var service = new AssessmentToolService(fixture, Options.Create(new AssessmentToolOptions { ReferenceSigningKey = new string('a', 40) }),
            renderer, new EphemeralIReGoReportArtifactStore(imageOptions), imageOptions);
        var context = new TrustedPatientContext { PatientId = 100 };
        var overview = await service.ReadAsync("patient_context", context, new(), "smoke", default);
        Assert.Equal(6, overview.Data!.Counts["record_count"]); // online game excluded
        Assert.Equal(3, overview.Data.Counts["readable_total_count"]);
        Assert.Equal(13, overview.Data.ScaleCoverage.Count);
        var history = await service.ReadAsync("patient_history", context, new(), "smoke", default);
        Assert.DoesNotContain("PRIVATE_ANSWER", JsonConvert.SerializeObject(history.Data));
        Assert.Contains(history.Data!.Records, r => r.ResultState == "identity_conflict");
        Assert.Contains(history.Data.Records, r => r.ResultState == "conflicting");
        Assert.Contains(history.Data.Records, r => r.SchemaFamily == "lowercase_total_v1" && r.ReportedScore == 12m);
        var reference = history.Data.Records.Single(r => r.ScaleType == 1 && r.ReportedScore.HasValue).AssessmentRef;
        var single = await service.ReadAsync("result_analysis", context, new() { Selector = "assessment_ref", AssessmentRef = reference, ScaleType = 1 }, "smoke", default);
        var singleReport = await service.ReportAsync(false, context,
            new() { Selector = "assessment_ref", AssessmentRef = reference, ExpectedSourceSnapshot = single.Data!.SourceSnapshot }, "https://example.test", "smoke", default);
        Assert.NotNull(singleReport.Data);
        var multiple = await service.ReadAsync("longitudinal_analysis", context, new() { ScaleType = 1, ReportCount = 4 }, "smoke", default);
        Assert.Contains(multiple.Data!.Records, r => r.ResultState == "no_result");
        Assert.Equal("blocked_unverified_scoring_rules", multiple.Data.Comparability);
        var multipleReport = await service.ReportAsync(true, context,
            new() { ScaleType = 1, ReportCount = 4, ExpectedSourceSnapshot = multiple.Data.SourceSnapshot }, "https://example.test", "smoke", default);
        Assert.NotNull(multipleReport.Data);
        Assert.Equal(2, renderer.Calls);
        fixture.Revision = "changed";
        var changed = await service.ReportAsync(false, context,
            new() { Selector = "assessment_ref", AssessmentRef = reference, ExpectedSourceSnapshot = single.Data.SourceSnapshot }, "https://example.test", "smoke", default);
        Assert.Contains("ASSESSMENT_SOURCE_CHANGED", changed.ReasonCodes);
        Assert.Equal(2, renderer.Calls);
        var denied = await service.ReadAsync("result_analysis", new() { PatientId = 999 }, new() { Selector = "assessment_ref", AssessmentRef = reference }, "smoke", default);
        Assert.Null(denied.Data);
        var preview = Environment.GetEnvironmentVariable("TRAINING_PREVIEW_DIR");
        if (!string.IsNullOrEmpty(preview))
        {
            Directory.CreateDirectory(preview);
            File.WriteAllText(Path.Combine(preview, "assessment-multiple.html"), renderer.Html);
        }
    }

    [Fact]
    public void Tool_family_catalog_smoke()
    {
        var ready = PatientToolFamilies.All.Where(f => f.ImplementationStatus == "implemented").ToArray();
        Assert.Equal(4, ready.Length);
        Assert.All(ready, f => Assert.Equal(6, f.Tools.Count));
        Assert.Equal(24, ready.SelectMany(f => f.Tools.Values).Distinct().Count());
        Assert.Empty(PatientToolFamilies.All.Single(f => f.ObjectType == "iremo").Tools);
    }

    /// <summary>
    /// Reproduces the three shapes actually found in project.dbreport: the legacy
    /// name/scoreList summary, the current TableTitle/Options summary, and the MoCA-B
    /// variant whose option-score array is spelled OptionsScore.
    /// </summary>
    [Fact]
    public async Task Real_world_report_shapes_are_readable()
    {
        var fixture = new RealWorldFixture();
        var renderer = new Renderer();
        var imageOptions = Options.Create(new IReGoToolOptions { ReportImage = new() { SigningKey = new string('r', 40) } });
        var service = new AssessmentToolService(fixture, Options.Create(new AssessmentToolOptions { ReferenceSigningKey = new string('a', 40) }),
            renderer, new EphemeralIReGoReportArtifactStore(imageOptions), imageOptions);
        var context = new TrustedPatientContext { PatientId = 100 };
        var history = await service.ReadAsync("patient_history", context, new(), "smoke", default);

        var moca = history.Data!.Records.Single(r => r.ScaleType == 11);
        Assert.Equal("catalog_and_report_match", moca.IdentityStatus);
        Assert.Equal("蒙特利尔认知评估量表记录表", moca.ScaleName);
        Assert.Equal("options_score_total_v1", moca.SchemaFamily);
        Assert.Equal(24m, moca.ReportedScore);
        Assert.Equal(30, moca.QuestionCount);
        Assert.Equal(30, moca.AnsweredItemCount!.Value);
        Assert.True(moca.HasItemAnswers);
        Assert.Null(moca.RecomputedScore); // the MoCA-B scoring rule is not signed off yet
        Assert.Empty(moca.Items); // history must not carry the per-item payload
        Assert.NotEmpty(moca.ReasonNotes);

        var ad8 = history.Data.Records.Single(r => r.SchemaFamily == "legacy_scale_summary_v1");
        Assert.Equal("AD8认知障碍自评表", ad8.ScaleName);
        Assert.Equal(3m, ad8.ReportedScore);
        Assert.Equal(8m, ad8.ScaleMaxScore!.Value);
        Assert.Equal(8, ad8.QuestionCount);
        Assert.Equal(8, ad8.AnsweredItemCount!.Value);
        Assert.Equal(3m, ad8.RecomputedScore!.Value);
        Assert.Contains("ASSESSMENT_SCORE_RECOMPUTED", ad8.ReasonCodes);

        // Single-record analysis is the only operation that carries per-item detail.
        var single = await service.ReadAsync("result_analysis", context,
            new() { Selector = "assessment_ref", AssessmentRef = ad8.AssessmentRef }, "smoke", default);
        var detail = single.Data!.Records.Single();
        Assert.Equal(8, detail.Items.Count);
        Assert.Equal(1, detail.Items[0].Index);
        Assert.Equal(0, detail.Items[0].OptionIndex);
        Assert.Equal(1m, detail.Items[0].Score);
        Assert.Equal(1m, detail.Items[0].MaxScore);
        Assert.True(detail.Items[0].Answered);
        Assert.Equal(1, detail.Items[1].OptionIndex);
        Assert.Equal(0m, detail.Items[1].Score);
        Assert.Contains(detail.ReasonNotes, note => note.Contains("重算"));
    }

    private sealed class Fixture : IAssessmentDataRepository
    {
        public string Revision = "original";
        public Task<AssessmentSource> LoadAsync(TrustedPatientContext context, CancellationToken cancellationToken)
        {
            var plans = Enumerable.Range(1, 7).Select(i => new RehabPlanEntity
            {
                Id = i, UserId = 100, DoctorId = i == 1 ? 100 : 200, BusinessId = 1, DeviceId = -1, TemplateId = 0,
                BookingTime = new DateTime(2026, 1, i),
                Details = JsonConvert.SerializeObject(new { type = i is 2 or 3 ? i : 1, name = i == 4 ? "饥饿的小鸟" : AssessmentScaleCatalog.Names[i is 2 or 3 ? i : 1] })
            }).ToArray();
            string Report(int type, bool lower = false) => lower
                ? JsonConvert.SerializeObject(new { tableTitle = AssessmentScaleCatalog.Names[type], title = new[] { "PRIVATE_ANSWER" }, score = 12, note = Revision })
                : JsonConvert.SerializeObject(new { TableTitle = AssessmentScaleCatalog.Names[type], Title = new[] { "PRIVATE_ANSWER" }, Score = 12, OptionsScore = new[] { 1 }, note = Revision });
            var reports = new[]
            {
                new RehabReportEntity { Id = 1, PlanId = 1, ReportDetails = Report(1) },
                new RehabReportEntity { Id = 2, PlanId = 2, ReportDetails = Report(2) },
                new RehabReportEntity { Id = 3, PlanId = 3, ReportDetails = Report(3, true) },
                new RehabReportEntity { Id = 6, PlanId = 6, ReportDetails = Report(2) },
                new RehabReportEntity { Id = 7, PlanId = 7, ReportDetails = Report(1) },
                new RehabReportEntity { Id = 8, PlanId = 7, ReportDetails = "{\"Score\":99}" }
            };
            return Task.FromResult(new AssessmentSource { Patient = new() { Id = 100, Status = 1, IsDeleted = 0, BusinessId = 1 }, Plans = plans, Reports = reports });
        }
    }
    private sealed class RealWorldFixture : IAssessmentDataRepository
    {
        public Task<AssessmentSource> LoadAsync(TrustedPatientContext context, CancellationToken cancellationToken)
        {
            var plans = new[]
            {
                new RehabPlanEntity
                {
                    Id = 11, UserId = 100, DoctorId = 200, BusinessId = 1, DeviceId = -1, TemplateId = 0,
                    BookingTime = new DateTime(2026, 3, 1),
                    Details = JsonConvert.SerializeObject(new { type = 11, name = "蒙特利尔认知评估量表记录表" })
                },
                new RehabPlanEntity
                {
                    Id = 12, UserId = 100, DoctorId = 200, BusinessId = 1, DeviceId = -1, TemplateId = 0,
                    BookingTime = new DateTime(2026, 1, 5),
                    // Legacy plans were written before the name field was populated.
                    Details = JsonConvert.SerializeObject(new { type = 1, name = "" })
                }
            };
            var mocaReport = JsonConvert.SerializeObject(new
            {
                Score = 24,
                Title = Enumerable.Range(1, 30).Select(i => $"Q{i}").ToArray(),
                Options = Enumerable.Range(1, 30).Select(_ => "是|不是|不知道").ToArray(),
                TableTitle = "蒙特利尔认知评估量表 (MoCA-B)",
                OptionsScore = Enumerable.Range(1, 30).Select(_ => "1|0|0.5").ToArray(),
                ScoreExplain = "源报告说明",
                // Fixed-length array padded with -1, exactly as the product writes it.
                OptionProblem = Enumerable.Range(0, 30).Select(i => i % 3).Concat(Enumerable.Repeat(-1, 60)).ToArray()
            });
            var ad8Report = JsonConvert.SerializeObject(new
            {
                name = "AD8认知障碍自评表",
                score = 3,
                scoreList = Enumerable.Repeat("1|0|0.5", 8).ToArray(),
                answerList = Enumerable.Repeat("是，有改变|不是，没有改变|不知道", 8).ToArray(),
                selectList = new[] { 0, 1, 1, 0, 1, 1, 0, 1 },
                questionList = Enumerable.Range(1, 8).Select(i => $"Q{i}").ToArray()
            });
            var reports = new[]
            {
                new RehabReportEntity { Id = 21, PlanId = 11, ReportDetails = mocaReport, CreateTime = new DateTime(2026, 3, 2) },
                new RehabReportEntity { Id = 22, PlanId = 12, ReportDetails = ad8Report, CreateTime = new DateTime(2026, 1, 6) }
            };
            return Task.FromResult(new AssessmentSource { Patient = new() { Id = 100, Status = 1, IsDeleted = 0, BusinessId = 1 }, Plans = plans, Reports = reports });
        }
    }
    private sealed class Renderer : IIReGoReportImageRenderer
    {
        public int Calls;
        public string Html = "";
        public Task<byte[]> RenderPngAsync(string html, CancellationToken cancellationToken = default)
        { Calls++; Html = html; return Task.FromResult(new byte[] { 1, 2, 3 }); }
    }
}
