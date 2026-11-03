using AI_WebApi.Model.Domain.IReGo;
using AI_WebApi.Repository.AITools;
using AI_WebApi.Services.AITools;

namespace AI_WebApi.Tests;

public sealed class PatientLayerSchemaScopeTests
{
    private static TrustedPatientContext Context(string scope = "current_tenant") => new()
    {
        PatientId = 7, Access = new PatientAccessContext(PatientAccessContextTests.Key)
        { UserIds = new[] { 7, 8 }, CurrentBusinessId = 1, DataScope = scope, ScopeFingerprint = "scope" }
    };

    private static void Prepare(PatientAccessTestDatabase db)
    {
        foreach (var sql in new[]
        {
            "CREATE TABLE dbmedical(Id INTEGER PRIMARY KEY,UserId INTEGER,TenantId INTEGER,Enable INTEGER,CreateTime TEXT,MedicalRecord TEXT,MedicalContent TEXT,Summary TEXT,ArrTime TEXT,ArrHospital TEXT,PrescriptionId INTEGER)",
            "CREATE TABLE dbconsultationinfo(Id INTEGER PRIMARY KEY,PatientId INTEGER,MedicalId INTEGER,BusinessId TEXT,CreateTime TEXT,StartTime TEXT,EndTime TEXT)",
            "CREATE TABLE dbprescription(Id INTEGER PRIMARY KEY,Name TEXT,Indication TEXT,Prescription TEXT,OfflineTrain TEXT,OnlineEvaluation TEXT,Enable INTEGER)",
            "CREATE TABLE dbmedicalprescription(MedicalId INTEGER,PrescriptionId INTEGER,IsDeleted INTEGER)",
            "INSERT INTO dbmedical VALUES(1,7,1,1,'2026-10-01','A','{}',NULL,NULL,NULL,10),(2,7,2,1,'2026-10-03','B','{}',NULL,NULL,NULL,11),(3,7,1,0,'2026-10-04','disabled','{}',NULL,NULL,NULL,12),(4,9,1,1,'2026-10-05','other','{}',NULL,NULL,NULL,13)",
            "INSERT INTO dbconsultationinfo VALUES(1,7,1,'1','2026-10-01',NULL,'1900-01-01'),(2,7,2,'2','2026-10-03',NULL,NULL),(3,7,3,'1','2026-10-04',NULL,NULL),(4,7,4,'1','2026-10-05',NULL,NULL),(5,7,2,'1','2026-10-06',NULL,NULL),(6,7,NULL,'1,2','2026-10-07',NULL,NULL)",
            "INSERT INTO dbprescription VALUES(10,'direct',NULL,NULL,NULL,NULL,1),(11,'cross',NULL,NULL,NULL,NULL,1),(12,'disabled-parent',NULL,NULL,NULL,NULL,1),(13,'other-owner',NULL,NULL,NULL,NULL,1),(14,'linked',NULL,NULL,NULL,NULL,1),(15,'deleted-link',NULL,NULL,NULL,NULL,1),(16,'disabled-prescription',NULL,NULL,NULL,NULL,0)",
            "INSERT INTO dbmedicalprescription VALUES(1,14,0),(1,15,1),(1,16,0)"
        }) db.Db.Ado.ExecuteCommand(sql);
    }

    [Fact]
    public async Task Current_institution_filters_rows_counts_prescriptions_and_string_consultation_owners()
    {
        using var db = new PatientAccessTestDatabase(); Prepare(db);
        var repository = new PatientLayerRepository(db);
        var medical = await repository.MedicalAsync(Context(), 1, 1, default);
        Assert.Equal(1, medical.Count); Assert.Equal(1, Assert.Single(medical.Rows).RecordId);
        Assert.Equal("verified", medical.Rows[0].RecordTenant!.AttributionStatus);
        var visits = await repository.ConsultationsAsync(Context(), 1, 20, default);
        Assert.Equal(1, visits.Count); Assert.Equal(1, Assert.Single(visits.Rows).RecordId);
        Assert.Null(visits.Rows[0].EndedAt);
        var prescriptions = await repository.PrescriptionsAsync(Context(), 1, default);
        Assert.Equal(new[] { 10, 14 }, prescriptions.Select(p => p.PrescriptionId));
        foreach (var id in new[] { 2, 3, 4 })
            Assert.Empty(await repository.PrescriptionsAsync(Context(), id, default));
        var all = await repository.MedicalAsync(Context("all_tenants"), 1, 20, default);
        Assert.Equal(2, all.Count); Assert.Equal(new[] { 2, 1 }, all.Rows.Select(r => r.RecordId));
    }

    [Fact]
    public void Current_profile_shape_projects_nested_clinical_fields_without_private_contacts()
    {
        const string source = """
            {"age":60,"sex":"女","birthday":"1966-03-04","name":"PRIVATE","phone":"PRIVATE",
             "patientInfo":{"height":165,"weight":60,"hospitalId":1,"departmentName":"康复科","roomNumber":"A","bedNumber":"12",
              "familyHistory":"源家庭史","operationTime":"1900-01-01","rehabilitationDetails":"源康复记录","degree":"高中",
              "homeAddress":"PRIVATE","identityId":"PRIVATE","contactInfo":"PRIVATE",
              "injuryDetails":{"state":"源受伤记录","injuredArea":2,"injuredTime":"2026-01-01","private":"PRIVATE"}}}
            """;
        var profile = PatientLayerService.ProfileFacts(source);
        Assert.Equal(60m, profile["age"]); Assert.Equal("1966-03-04", profile["birth_date"]);
        Assert.Equal("源家庭史", profile["self_reported_family_history"]);
        Assert.False(profile.ContainsKey("operation_date"));
        Assert.DoesNotContain("PRIVATE", Newtonsoft.Json.JsonConvert.SerializeObject(profile));
        var matching = PatientLayerService.CareContextFacts(source, 1);
        Assert.Equal("康复科", matching["department_name"]);
        Assert.Empty(PatientLayerService.CareContextFacts(source, 2));
    }
}
