using AI_WebApi.Model.Entities;
using AI_WebApi.Repository.AITools;
using AI_WebApi.Repository.BASE;
using AI_WebApi.Repository.UnitOfWorks;
using SqlSugar;

namespace AI_WebApi.Tests;

public sealed class ProjectRepositoryTests
{
    [Fact]
    public async Task Current_identity_uses_roles_and_unique_phone_without_names_or_old_tables()
    {
        using var fixture = new Fixture();
        var db = fixture.Db;
        db.Insertable(new[]
        {
            new PatientEntity { Id = 7, Phone = "test-unique", Name = null, IsDeleted = false },
            new PatientEntity { Id = 8, Phone = "test-shared", IsDeleted = false },
            new PatientEntity { Id = 9, Phone = "test-shared", IsDeleted = null },
            new PatientEntity { Id = 10, Phone = "test-deleted", IsDeleted = true },
            new PatientEntity { Id = 11, Phone = "test-doctor", IsDeleted = false }
        }).OffIdentity().ExecuteCommand();
        db.Insertable(new[] { Role(7, 0), Role(7, 0), Role(7, 1), Role(8, 0), Role(9, 0), Role(10, 0), Role(11, 1) }).ExecuteCommand();
        var repository = new PatientIdentityMappingRepository(fixture);
        Assert.Equal(new[] { 7 }, await repository.FindRobotPatientCandidatesAsync(7));
        var phone = Assert.Single(await repository.FindRobotPatientCandidatesByPhoneAsync("test-unique"));
        Assert.Equal(7, phone.ProjectPatientId);
        Assert.Equal(phone.ProjectPatientId, phone.RobotPatientId);
        Assert.Equal(2, (await repository.FindRobotPatientCandidatesByPhoneAsync("test-shared")).Count);
        foreach (var id in new[] { 10, 11, 1000007 }) Assert.Empty(await repository.FindRobotPatientCandidatesAsync(id));
        Assert.Empty(await repository.FindRobotPatientCandidatesByPhoneAsync("test-doctor"));
        Assert.Empty(await repository.FindRobotPatientCandidatesByPhoneAsync(" "));
        Assert.Empty(await repository.FindRobotPatientCandidatesByPhoneAsync("' OR 1=1 --"));
    }

    [Fact]
    public async Task Irego_adapter_uses_current_business_roles_and_nullable_creation_time()
    {
        using var fixture = new Fixture();
        var db = fixture.Db;
        db.Insertable(new[]
        {
            new PatientEntity { Id = 7, BusinessId = 3, UserDetails = "{}", IsDeleted = false },
            new PatientEntity { Id = 8, BusinessId = 3, IsDeleted = false },
            new PatientEntity { Id = 9, BusinessId = 4, IsDeleted = false },
            new PatientEntity { Id = 10, BusinessId = 3, IsDeleted = true }
        }).OffIdentity().ExecuteCommand();
        db.Insertable(new[] { Role(7, 0), Role(7, 0), Role(8, 1), Role(9, 1), Role(10, 0) }).ExecuteCommand();
        db.Insertable(new RobotDbMainVendorEntity { Id = 3 }).OffIdentity().ExecuteCommand();
        db.Insertable(new RobotDbMainTemplateEntity { Id = 5, ModelId = 1, Details = "{}" }).OffIdentity().ExecuteCommand();
        // Insert explicit IDs: production IDs are retained in the repository output.
        db.Ado.ExecuteCommand("INSERT INTO rehabplan (Id,doctorid,PatientId,devicetype,StartTime,CompleteStatus,Details,createtime,templateid) VALUES (11,8,7,1,'2026-09-01',1,'{}',NULL,5),(12,9,7,1,'2026-09-01',1,'{}',NULL,5),(13,8,7,1,'2026-09-01',1,'{}',NULL,999)");
        db.Insertable(new RobotDbMainReportEntity { Id = 11, Details = "{}" }).ExecuteCommand();
        var data = await Repository(fixture).LoadPatientDataAsync(7);
        Assert.Equal(7, data.Patient!.Id);
        Assert.Equal(3, data.Patient.BusinessId);
        Assert.Equal("{}", data.Patient.UserDetails);
        var plan = Assert.Single(data.Plans);
        Assert.Equal(11, plan.Id);
        Assert.Null(plan.CreateTime);
        Assert.Equal(11, Assert.Single(data.Reports).PlanId);
        Assert.Equal(2, data.ReferenceAnomalyCount);
        Assert.Null((await Repository(fixture).LoadPatientDataAsync(10)).Patient);
        Assert.Null((await Repository(fixture).LoadPatientDataAsync(8)).Patient);
        Assert.DoesNotContain(db.DbMaintenance.GetColumnInfosByTableName("rehabplan", false),
            column => column.DbColumnName.Equals("updatetime", StringComparison.OrdinalIgnoreCase));
    }

    [ProjectDatabaseFact]
    [Trait("Category", "ProjectReadOnly")]
    public async Task Actual_project_schema_supports_both_repositories_in_a_readonly_transaction()
    {
        using var db = new SqlSugarScope(new ConnectionConfig
        {
            ConfigId = "project_main", DbType = DbType.MySql, InitKeyType = InitKeyType.Attribute,
            IsAutoCloseConnection = false, ConnectionString = Environment.GetEnvironmentVariable("AIWEBAPI_TEST_PROJECT_CONNECTION")!
        });
        db.Ado.ExecuteCommand("SET SESSION TRANSACTION READ ONLY");
        db.Ado.BeginTran();
        try
        {
            Assert.Equal("project", db.Ado.GetString("SELECT DATABASE()"));
            var id = db.Ado.GetInt("SELECT p.PatientId FROM rehabplan p JOIN dbuser u ON u.Id=p.PatientId WHERE COALESCE(u.IsDeleted,0)=0 AND EXISTS(SELECT 1 FROM dbroleuser r WHERE r.UserId=u.Id AND r.RoleId=0) GROUP BY p.PatientId ORDER BY COUNT(*) DESC LIMIT 1");
            var unit = new ReadOnlyUnit(db);
            var identity = new PatientIdentityMappingRepository(unit);
            Assert.Equal(new[] { id }, await identity.FindRobotPatientCandidatesAsync(id));
            var data = await Repository(unit).LoadPatientDataAsync(id);
            Assert.Equal(id, data.Patient!.Id);
            Assert.All(data.Plans, plan => Assert.Equal(id, plan.UserId));
            var phone = db.Queryable<PatientEntity>().Where(x => x.Id == id).Select(x => x.Phone).First();
            if (!string.IsNullOrWhiteSpace(phone))
                Assert.All(await identity.FindRobotPatientCandidatesByPhoneAsync(phone),
                    candidate => Assert.Equal(candidate.ProjectPatientId, candidate.RobotPatientId));
        }
        finally { db.Ado.RollbackTran(); }
    }

    private static DbRoleUserEntity Role(int user, int role) => new() { UserId = user, RoleId = role };

    private static IReGoDataRepository Repository(IUnitOfWorkManage unit) => new(
        new BaseRepository<RobotDbMainUserEntity>(unit), new BaseRepository<RobotDbMainRehabPlanEntity>(unit),
        new BaseRepository<RobotDbMainReportEntity>(unit), new BaseRepository<RobotDbMainTemplateEntity>(unit),
        new BaseRepository<RobotDbMainVendorEntity>(unit), new BaseRepository<RobotDbMainReportDataNewEntity>(unit),
        new BaseRepository<RobotDbMainLegacyReportDataEntity>(unit));

    private class ReadOnlyUnit : IUnitOfWorkManage
    {
        private readonly SqlSugarScope _db;
        public ReadOnlyUnit(SqlSugarScope db) => _db = db;
        public SqlSugarScope GetDbClient() => _db;
        public int TranCount => 0;
        public void BeginTran() => throw new NotSupportedException();
        public void CommitTran() => throw new NotSupportedException();
        public void RollbackTran() => throw new NotSupportedException();
    }

    private sealed class Fixture : IUnitOfWorkManage, IDisposable
    {
        public SqlSugarScope Db { get; } = new(new ConnectionConfig
        { DbType = DbType.Sqlite, ConnectionString = "Data Source=:memory:", IsAutoCloseConnection = false, InitKeyType = InitKeyType.Attribute,
            ConfigureExternalServices = new ConfigureExternalServices
            {
                EntityService = (property, column) =>
                {
                    if (property.PropertyType == typeof(string) || property.PropertyType == typeof(byte[]) ||
                        Nullable.GetUnderlyingType(property.PropertyType) != null)
                        column.IsNullable = true;
                }
            }
        });
        public Fixture()
        {
            Db.CodeFirst.InitTables(typeof(PatientEntity), typeof(DbRoleUserEntity), typeof(RobotDbMainVendorEntity),
                typeof(RobotDbMainRehabPlanEntity), typeof(RobotDbMainTemplateEntity), typeof(RobotDbMainReportEntity),
                typeof(RobotDbMainReportDataNewEntity), typeof(RobotDbMainLegacyReportDataEntity));
        }
        public SqlSugarScope GetDbClient() => Db;
        public int TranCount => 0;
        public void BeginTran() => throw new NotSupportedException();
        public void CommitTran() => throw new NotSupportedException();
        public void RollbackTran() => throw new NotSupportedException();
        public void Dispose() => Db.Dispose();
    }
}

public sealed class ProjectDatabaseFactAttribute : FactAttribute
{
    public ProjectDatabaseFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AIWEBAPI_TEST_PROJECT_CONNECTION")))
            Skip = "Set AIWEBAPI_TEST_PROJECT_CONNECTION to opt into the read-only project database check.";
    }
}
