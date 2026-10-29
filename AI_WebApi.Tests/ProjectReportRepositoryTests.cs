using AI_WebApi.Model.Entities;
using AI_WebApi.Repository.AITools;
using AI_WebApi.Repository.UnitOfWorks;
using SqlSugar;

namespace AI_WebApi.Tests;

public sealed class ProjectReportRepositoryTests
{
    [Fact]
    public async Task Patient_history_retains_old_institutions_and_excludes_other_patients_and_devices()
    {
        using var unit = new Unit();
        var db = unit.Db;
        db.Insertable(new[] {
            new PatientEntity { Id=7, BusinessId=3, Status=1, IsDeleted=false },
            new PatientEntity { Id=8, BusinessId=4, Status=1, IsDeleted=false },
            new PatientEntity { Id=9, BusinessId=3, Status=1, IsDeleted=false }
        }).OffIdentity().ExecuteCommand();
        db.Insertable(new[] { new DbRoleUserEntity { UserId=7, RoleId=0 },
            new DbRoleUserEntity { UserId=8, RoleId=0 }, new DbRoleUserEntity { UserId=9, RoleId=1 } }).ExecuteCommand();
        db.Insertable(new[] {
            new IReTourDeviceEntity { Id=1, BusinessId=3, DeviceType=1 },
            new IReTourDeviceEntity { Id=2, BusinessId=4, DeviceType=1 },
            new IReTourDeviceEntity { Id=3, BusinessId=3, DeviceType=5 }
        }).OffIdentity().ExecuteCommand();
        db.Insertable(new RehabTemplateEntity { Id=4, ModelId=0 }).OffIdentity().ExecuteCommand();
        db.Insertable(new[] {
            Plan(11,7,3,1), Plan(12,7,3,2), Plan(13,7,3,3), Plan(14,8,4,1)
        }).OffIdentity().ExecuteCommand();
        db.Insertable(new[] {
            new RehabReportEntity { Id=21, PlanId=11, ReportDetails="{}" },
            new RehabReportEntity { Id=22, PlanId=12, ReportDetails="{}" },
            new RehabReportEntity { Id=23, PlanId=14, ReportDetails="{}" }
        }).OffIdentity().ExecuteCommand();
        var raw = await new IReGoDataRepository(unit).LoadPatientDataAsync(7);
        Assert.Equal("project", raw.SourceSystem);
        Assert.Equal(new[] {11,12}, raw.Plans.Select(p => p.Id).OrderBy(id => id));
        Assert.Equal(new int?[] {11,12}, raw.Reports.Select(r => r.PlanId).OrderBy(id => id));
        Assert.Single(raw.SourceIssues);
        Assert.Equal(0, raw.Templates[4].ModelId);
        Assert.Null((await new IReGoDataRepository(unit).LoadPatientDataAsync(9)).Patient);
    }

    private static RehabPlanEntity Plan(int id,int patient,int institution,int device) =>
        new() { Id=id, UserId=patient, BusinessId=institution, DeviceId=device, TemplateId=4 };

    private sealed class Unit : IUnitOfWorkManage, IDisposable
    {
        public SqlSugarScope Db { get; } = new(new ConnectionConfig {
            ConfigId="project_main", DbType=DbType.Sqlite, ConnectionString="Data Source=:memory:",
            IsAutoCloseConnection=false, InitKeyType=InitKeyType.Attribute,
            ConfigureExternalServices=new ConfigureExternalServices {
                EntityService=(property,column) => {
                    if (property.PropertyType==typeof(string) || Nullable.GetUnderlyingType(property.PropertyType)!=null)
                        column.IsNullable=true;
                }
            }
        });
        public Unit() => Db.CodeFirst.InitTables(typeof(PatientEntity),typeof(DbRoleUserEntity),
            typeof(IReTourDeviceEntity),typeof(RehabPlanEntity),typeof(RehabReportEntity),typeof(RehabTemplateEntity));
        public SqlSugarScope GetDbClient() => Db;
        public int TranCount => 0;
        public void BeginTran() => throw new NotSupportedException();
        public void CommitTran() => throw new NotSupportedException();
        public void RollbackTran() => throw new NotSupportedException();
        public void Dispose() => Db.Dispose();
    }
}
