using AI_WebApi.Model.Entities;
using AI_WebApi.Repository.UnitOfWorks;
using SqlSugar;

namespace AI_WebApi.Tests;

/// <summary>Isolated synthetic data only; never opens project or a configured connection.</summary>
internal sealed class PatientAccessTestDatabase : IUnitOfWorkManage, IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ai-webapi-access-tests-" + Guid.NewGuid().ToString("N"));
    public SqlSugarScope Db { get; }
    public PatientAccessTestDatabase()
    {
        Directory.CreateDirectory(_directory);
        Db = new(new ConnectionConfig
        {
            ConfigId = "project_main", DbType = DbType.Sqlite,
            ConnectionString = "Data Source=" + Path.Combine(_directory, "fixture.db") + ";Pooling=False",
            IsAutoCloseConnection = true, InitKeyType = InitKeyType.Attribute,
            ConfigureExternalServices = new ConfigureExternalServices
            {
                EntityService = (property, column) =>
                {
                    if (property.PropertyType == typeof(string) || Nullable.GetUnderlyingType(property.PropertyType) != null)
                        column.IsNullable = true;
                }
            }
        });
        Db.CodeFirst.InitTables(typeof(PatientEntity), typeof(DbRoleUserEntity), typeof(PatientTenantEntity),
            typeof(PatientInstitutionEntity), typeof(RehabPlanEntity), typeof(RehabReportEntity), typeof(IReTourDeviceEntity),
            typeof(RehabTemplateEntity), typeof(IReTourWalkEntity), typeof(IReTourSummaryReportEntity),
            typeof(IReTourRawReportEntity), typeof(IReTourCognitiveReportEntity), typeof(IReTourPatientBindingEntity));
        Db.Insertable(new[]
        {
            Account(7, 0, "000007"), Account(8, 2, "000008"),
            new PatientEntity { Id=9, BusinessId=0, LoginName="000009", Name="患者乙", Phone="13800000002", Status=1, IsDeleted=false, UserDetails="{}" }
        }).OffIdentity().ExecuteCommand();
        Db.Insertable(new[]
        {
            new DbRoleUserEntity { UserId=7,RoleId=0,BusinessId=1,Enable=0 },
            new DbRoleUserEntity { UserId=7,RoleId=0,BusinessId=2,Enable=0 },
            new DbRoleUserEntity { UserId=8,RoleId=0,BusinessId=2,Enable=0 },
            new DbRoleUserEntity { UserId=9,RoleId=0,BusinessId=1,Enable=0 }
        }).ExecuteCommand();
        Db.Insertable(new[]
        {
            new PatientTenantEntity { UserId=7,BusinessId=1,Enable=0 },
            new PatientTenantEntity { UserId=7,BusinessId=2,Enable=0 },
            new PatientTenantEntity { UserId=7,BusinessId=2,Enable=0 },
            new PatientTenantEntity { UserId=8,BusinessId=2,Enable=0 }
        }).ExecuteCommand();
        Db.Insertable(new[] { new PatientInstitutionEntity { Id=1,Name="A医院" },
            new PatientInstitutionEntity { Id=2,Name="B卫生服务中心" } }).OffIdentity().ExecuteCommand();
        Db.Insertable(new[] { new IReTourDeviceEntity { Id=1,BusinessId=1,DeviceType=1 },
            new IReTourDeviceEntity { Id=2,BusinessId=2,DeviceType=1 },
            new IReTourDeviceEntity { Id=3,BusinessId=1,DeviceType=5 },
            new IReTourDeviceEntity { Id=4,BusinessId=2,DeviceType=6 } }).OffIdentity().ExecuteCommand();
        Db.Insertable(new RehabTemplateEntity { Id=4,ModelId=0 }).OffIdentity().ExecuteCommand();
        Db.Insertable(new[] { Plan(11,7,1,1,1), Plan(12,7,2,2,2), Plan(13,8,2,2,3),
            Plan(14,9,1,1,4), Plan(15,7,0,1,5), Plan(16,7,1,3,6) }).OffIdentity().ExecuteCommand();
        Db.Insertable(new[] {
            new RehabReportEntity { Id=21,PlanId=11 }, new RehabReportEntity { Id=22,PlanId=12 },
            new RehabReportEntity { Id=23,PlanId=13 }, new RehabReportEntity { Id=24,PlanId=14 },
            new RehabReportEntity { Id=26,PlanId=16,CreateTime=new DateTime(2026,10,6,10,0,0),
                ReportDetails="{\"Title\":[{\"item\":1}],\"TableTitle\":\"合成量表\",\"Score\":23}" }
        }).OffIdentity().ExecuteCommand();
    }
    private static PatientEntity Account(int id,int business,string login) => new()
    { Id=id,BusinessId=business,LoginName=login,Name="患者甲",Phone="13800000001",Status=1,IsDeleted=false,UserDetails="{}" };
    private static RehabPlanEntity Plan(int id,int owner,int tenant,int device,int day) => new()
    { Id=id,UserId=owner,BusinessId=tenant,DeviceId=device,TemplateId=4,Status=1,CreateTime=new DateTime(2026,10,day,9,0,0),BookingTime=new DateTime(2026,10,day,9,0,0) };
    public SqlSugarScope GetDbClient() => Db;
    public int TranCount => 0;
    public void BeginTran() => throw new NotSupportedException();
    public void CommitTran() => throw new NotSupportedException();
    public void RollbackTran() => throw new NotSupportedException();
    public void Dispose()
    {
        Db.Dispose();
        try { File.Delete(Path.Combine(_directory, "fixture.db")); Directory.Delete(_directory); }
        catch (IOException) { }
    }
}
