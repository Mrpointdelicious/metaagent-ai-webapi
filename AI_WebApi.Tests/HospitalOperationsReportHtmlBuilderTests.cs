using AI_WebApi.Model.DTOs.AITools;
using AI_WebApi.Services.AITools.HospitalOperations;

namespace AI_WebApi.Tests;

public sealed class HospitalOperationsReportHtmlBuilderTests
{
    [Fact]
    public void Full_report_keeps_sections_and_encodes_hospital_name()
    {
        var result = Result("all", "医院<script>alert(1)</script>");

        var html = new HospitalOperationsReportHtmlBuilder().Build(result);

        Assert.Contains("运营总览", html);
        Assert.Contains("问诊情况", html);
        Assert.Contains("虚拟诊室", html);
        Assert.Contains("评估与训练", html);
        Assert.Contains("问诊类型与业务资料", html);
        Assert.Contains("AI 智能分析", html);
        Assert.Contains("医院&lt;script&gt;alert(1)&lt;/script&gt;", html);
        Assert.DoesNotContain("医院<script>", html);
        Assert.Contains("稳定报表引用 · 可重复访问", html);
    }

    [Fact]
    public void Partial_report_only_renders_requested_business_section()
    {
        var html = new HospitalOperationsReportHtmlBuilder().Build(
            Result("rooms", "岳阳医院"));

        Assert.Contains("虚拟诊室", html);
        Assert.DoesNotContain("运营总览", html);
        Assert.DoesNotContain("评估与训练", html);
        Assert.DoesNotContain("问诊类型与业务资料", html);
    }

    [Fact]
    public void Pairwise_report_renders_both_hospitals_and_right_minus_left()
    {
        var result = Result("overview", "甲医院");
        result = new HospitalOperationsResultDto
        {
            Scope = "comparison",
            Section = result.Section,
            Period = result.Period,
            Hospitals = result.Hospitals,
            Comparison = new HospitalOperationsPairwiseComparisonDto
            {
                LeftHospital = new HospitalDirectoryEntryDto
                {
                    HospitalId = 13,
                    HospitalName = "甲医院"
                },
                RightHospital = new HospitalDirectoryEntryDto
                {
                    HospitalId = 25,
                    HospitalName = "乙医院"
                },
                Metrics = new[]
                {
                    new HospitalOperationsComparisonMetricDto
                    {
                        Section = "overview",
                        Code = "registered_patients",
                        Label = "注册患者",
                        LeftValue = 62,
                        RightValue = 74,
                        LeftDisplayValue = "62 人",
                        RightDisplayValue = "74 人",
                        Difference = 12,
                        DifferenceDisplayValue = "+12 人",
                        Unit = "人",
                        DifferenceUnit = "人",
                        TimeScope = "current_snapshot",
                        LargerValueHospitalId = 25
                    }
                },
                Notes = new[] { "差值统一按右侧医院减左侧医院计算。" }
            }
        };

        var html = new HospitalOperationsReportHtmlBuilder().Build(result);

        Assert.Contains("两院指标对照", html);
        Assert.Contains("甲医院", html);
        Assert.Contains("乙医院", html);
        Assert.Contains("差值（右－左）", html);
        Assert.Contains("+12 人", html);
    }

    private static HospitalOperationsResultDto Result(
        string section,
        string name) => new()
        {
            Scope = "institution",
            Section = section,
            Period = new HospitalOperationsPeriodDto
            {
                StartDate = "2026-08-01",
                EndDate = "2026-08-31"
            },
            Hospitals = new[]
            {
                new HospitalOperationsSnapshotDto
                {
                    Hospital = new HospitalDirectoryEntryDto
                    {
                        HospitalId = 13,
                        HospitalName = name,
                        TenantLevel = 3
                    },
                    Overview = new HospitalOverviewDto
                    {
                        PatientCount = 62,
                        DoctorCount = 46,
                        DailyActiveCount = 1
                    },
                    Consultation = new HospitalConsultationDto
                    {
                        ConsultationPeople = 49,
                        ConsultationTimes = 176
                    },
                    Rooms = new HospitalRoomsDto
                    {
                        ConsultationRooms = 8,
                        AssessmentRooms = 8,
                        TrainingRooms = 8
                    },
                    AssessmentTraining = new HospitalAssessmentTrainingDto(),
                    Records = new HospitalRecordsDto
                    {
                        RealConsultationCount = 79,
                        MedicalRecordCount = 181,
                        PrescriptionTemplateCount = 90,
                        ReportCount = 90
                    }
                }
            },
            Analysis = new[]
            {
                new HospitalOperationsAnalysisItemDto
                {
                    Code = "service_overview",
                    Title = "运营解读",
                    Text = "这是结构化事实生成的解读。",
                    HospitalId = 13
                }
            }
        };
}
