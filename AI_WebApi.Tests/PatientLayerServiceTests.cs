using AI_WebApi.IServices.AITools;
using AI_WebApi.Model.Domain.IReGo;
using AI_WebApi.Model.DTOs.AITools;
using AI_WebApi.Model.Entities;
using AI_WebApi.Repository.AITools;
using AI_WebApi.Services.AITools;

namespace AI_WebApi.Tests;

public sealed class PatientLayerServiceTests
{
    [Fact]
    public void ProfileProjectsKnownFieldsAndKeepsUnknownAgeMissing()
    {
        var facts = PatientLayerService.ProfileFacts("""
            {"patientInfo":{"age":0,"sex":"男","height":"170","medicalHistory":"左膝疼痛"},
             "phone":"sensitive","password":"sensitive","name":"private"}
            """);
        Assert.False(facts.ContainsKey("age"));
        Assert.Equal(170m, facts["height_cm"]);
        Assert.Equal("左膝疼痛", facts["self_reported_medical_history"]);
        Assert.DoesNotContain("sensitive", Newtonsoft.Json.JsonConvert.SerializeObject(facts));
        Assert.False(facts.ContainsKey("name"));
    }

    [Fact]
    public void MedicalContentKeepsDiagnosisAndExistingOrdersDistinct()
    {
        var facts = PatientLayerService.MedicalFacts("""
            {"DiseaseName":"已记录诊断","DoctorOrder":"原医嘱","DiagnosisResult":"检查结果"}
            """);
        Assert.Equal("已记录诊断", facts["diagnosis"]);
        Assert.Equal("原医嘱", facts["doctor_orders"]);
        Assert.Equal("检查结果", facts["diagnosis_result"]);
    }

    [Fact]
    public async Task MedicalLayerPreservesPagination()
    {
        var repo = new Repository(); var devices = new Devices();
        var service = new PatientLayerService(repo, devices, devices);
        var response = await service.ConsultationAsync(Context(2681),
            new PatientLayerRequestDto { PageNumber = 2, PageSize = 1 }, "r", default);
        Assert.Equal(PatientLayerContract.Version, response.ContractVersion);
        Assert.Equal(ToolStatuses.Success, response.Status);
        Assert.Equal("patient:2681", response.Data!.PatientRef);
        Assert.Equal(2, response.Data.Page.PageNumber);
        Assert.True(response.Data.Page.HasNext);
        Assert.Equal(3, response.Data.MedicalRecordCount);
        Assert.Equal("原医嘱", response.Data.MedicalRecords.Single().Content["doctor_orders"]);
    }

    [Fact]
    public async Task ProfileUsesTheSameScopedPatientReferenceAsOtherLayers()
    {
        var devices = new Devices();
        var service = new PatientLayerService(new Repository(), devices, devices);
        var context = new TrustedPatientContext
        {
            PatientId = 7, Access = new PatientAccessContext("unit-test-key")
            { ScopeFingerprint = "confirmed-scope", UserIds = new[] { 7, 8 } }
        };
        var response = await service.ProfileAsync(context, "r", default);
        Assert.Equal(context.PatientRef, response.Data!.PatientRef);
        Assert.StartsWith("patient_scope_v2_", response.Data.PatientRef);
    }

    [Fact]
    public async Task RehabUsesOneResolvedProjectContextAcrossDeviceFamilies()
    {
        var devices = new Devices();
        var service = new PatientLayerService(new Repository(), devices, devices);
        var response = await service.RehabAsync(Context(3799), new(), "r", default);
        Assert.Equal(ToolStatuses.Success, response.Status);
        Assert.Equal(ToolStatuses.Success, response.Data!.Iretour.Availability);
        Assert.Equal(ToolStatuses.Success, response.Data.Irego.Availability);
        Assert.Single(response.Data.Plans);
        Assert.Equal(3799, devices.TourPatient);
        Assert.Equal(3799, devices.GoPatient);
    }

    [Fact]
    public async Task OneFailedDeviceReturnsPartialAndRetainsOtherDevice()
    {
        var devices = new Devices { FailTour = true};
        var service = new PatientLayerService(new Repository(), devices, devices);
        var response = await service.RehabAsync(Context(1614), new(), "r", default);
        Assert.Equal(ToolStatuses.Partial, response.Status);
        Assert.Equal(ToolStatuses.Failed, response.Data!.Iretour.Availability);
        Assert.Equal(ToolStatuses.Success, response.Data.Irego.Availability);
        Assert.Equal(1614, devices.GoPatient);
    }

    [Fact]
    public async Task DeletedPatientIsUnavailableAndQueriesNoDevice()
    {
        var devices = new Devices();
        var service = new PatientLayerService(new Repository { Deleted = true }, devices, devices);
        var response = await service.RehabAsync(Context(3799), new(), "r", default);
        Assert.Equal(ToolStatuses.Unavailable, response.Status);
        Assert.Null(response.Data);
        Assert.Null(devices.TourPatient);
    }

    private static TrustedPatientContext Context(int id) => new() { PatientId = id, SubjectRef = "trusted", ConversationId = "c" };

    private sealed class Repository : IPatientLayerRepository
    {
        public bool Deleted { get; init; }
        public Task<PatientEntity?> PatientAsync(int id, CancellationToken ct) => Task.FromResult<PatientEntity?>(new() { Id = id, IsDeleted = Deleted });
        public Task<(int, IReadOnlyList<PatientMedicalRow>)> MedicalAsync(int id, int page, int size, CancellationToken ct) => Task.FromResult<(int, IReadOnlyList<PatientMedicalRow>)>((3, new[] { new PatientMedicalRow
        { RecordId = 1, MedicalContent = "{\"DiseaseName\":\"诊断\",\"DoctorOrder\":\"原医嘱\"}" } }));
        public Task<(int, IReadOnlyList<PatientConsultationRecordDto>)> ConsultationsAsync(int id, int page, int size, CancellationToken ct) => Task.FromResult<(int, IReadOnlyList<PatientConsultationRecordDto>)>((0, Array.Empty<PatientConsultationRecordDto>()));
        public Task<IReadOnlyList<PatientPrescriptionDto>> PrescriptionsAsync(int id, int medicalId, CancellationToken ct) => Task.FromResult<IReadOnlyList<PatientPrescriptionDto>>(Array.Empty<PatientPrescriptionDto>());
        public Task<(int, IReadOnlyList<PatientRehabPlanDto>)> PlansAsync(int id, int page, int size, CancellationToken ct) => Task.FromResult<(int, IReadOnlyList<PatientRehabPlanDto>)>((1, new[] { new PatientRehabPlanDto { PlanId = 1 } }));
    }

    private sealed class Devices : IIReTourPatientHistoryService, IIReGoPatientHistoryService
    {
        public bool FailTour { get; init; }
        public int? TourPatient { get; private set; }
        public int? GoPatient { get; private set; }
        public Task<ToolResponseDto<IReTourPatientHistoryDto>> GetAsync(TrustedPatientContext c, GetIReTourPatientHistoryRequestDto request, string rid, CancellationToken ct)
        {
            TourPatient = c.PatientId;
            if (FailTour) throw new InvalidOperationException("unavailable");
            return Task.FromResult(ToolResponseDto<IReTourPatientHistoryDto>.Create("get_iretour_patient_history", rid, ToolStatuses.Success, new(), null, new()));
        }
        public Task<ToolResponseDto<IReGoPatientHistoryDto>> GetAsync(TrustedPatientContext c, GetIReGoPatientHistoryRequestDto request, string rid, CancellationToken ct = default)
        {
            GoPatient = c.PatientId;
            return Task.FromResult(ToolResponseDto<IReGoPatientHistoryDto>.Create("get_irego_patient_history", rid, ToolStatuses.Success, new(), null, new()));
        }
    }
}
