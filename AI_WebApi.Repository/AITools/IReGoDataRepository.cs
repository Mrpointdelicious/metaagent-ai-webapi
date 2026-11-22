using AI_WebApi.Model.Domain.IReGo;
using AI_WebApi.Model.Entities;
using AI_WebApi.Model.DTOs.AITools;
using AI_WebApi.Repository.UnitOfWorks;
using SqlSugar;

namespace AI_WebApi.Repository.AITools;

/// <summary>SELECT-only IReGo records from project.dbrehaplan -> dbreport.</summary>
public sealed class IReGoDataRepository : IIReGoDataRepository
{
    private readonly SqlSugarScope _db;
    public IReGoDataRepository(IUnitOfWorkManage unit) => _db = unit.GetDbClient();

    public async Task<IReGoRawDataSet> LoadPatientDataAsync(
        TrustedPatientContext context, CancellationToken cancellationToken = default)
    {
        var patientId = context.PatientId;
        var access = context.RequireAccess();
        var db = _db.GetConnectionScope("project_main");
        var patient = await db.Queryable<PatientEntity>()
            .Where(p => p.Id == patientId && p.BusinessId == 0 && p.Status == 1 && p.IsDeleted == false &&
                SqlFunc.Subqueryable<DbRoleUserEntity>()
                    .Where(r => r.UserId == p.Id && r.Enable == 0 &&
                        (r.RoleId == DbRoleUserEntity.PatientRoleId || r.RoleId == DbRoleUserEntity.StudentRoleId)).Any())
            .FirstAsync(cancellationToken);
        if (patient == null) return new() { SourceSystem = "project" };
        var plans = await db.Queryable<RehabPlanEntity>()
            .InnerJoin<IReTourDeviceEntity>((p, d) => p.DeviceId == d.Id)
            // Historical plan ownership follows the canonical patient FK, not the
            // current institution of a patient/device that may have since moved.
            .Where((p, d) => p.UserId == patientId && d.DeviceType == 1)
            .WhereIF(access.DataScope == "current_tenant", (p, d) => p.BusinessId == access.CurrentBusinessId)
            .OrderBy((p, d) => p.Id, OrderByType.Desc).Select((p, d) => p)
            .Take(1001).ToListAsync(cancellationToken);
        if (plans.Count > 1000) throw new InvalidOperationException("IREGO_READ_LIMIT");
        foreach (var plan in plans)
        {
            plan.RecordTenant = access.Observe($"project.dbrehaplan:{plan.Id}", plan.BusinessId, "project.dbrehaplan.BusinessId", plan.UserId);
            plan.OwnerUserRef = access.Reference("owner", "project.dbuser|" + plan.UserId);
        }
        var ids = plans.Select(p => p.Id).ToArray();
        var reports = ids.Length == 0 ? new List<RehabReportEntity>() :
            await db.Queryable<RehabReportEntity>().Where(r => ids.Contains(r.PlanId ?? 0))
                .Take(5001).ToListAsync(cancellationToken);
        if (reports.Count > 5000) throw new InvalidOperationException("IREGO_READ_LIMIT");
        var deviceIds = plans.Where(p => p.DeviceId.HasValue).Select(p => p.DeviceId!.Value).Distinct().ToArray();
        var templateIds = plans.Where(p => p.TemplateId.HasValue).Select(p => p.TemplateId!.Value).Distinct().ToArray();
        var devices = deviceIds.Length == 0 ? new List<IReTourDeviceEntity>() :
            await db.Queryable<IReTourDeviceEntity>().Where(d => deviceIds.Contains(d.Id)).ToListAsync(cancellationToken);
        var templates = templateIds.Length == 0 ? new List<RehabTemplateEntity>() :
            await db.Queryable<RehabTemplateEntity>().Where(t => templateIds.Contains(t.Id)).ToListAsync(cancellationToken);
        return new()
        {
            Patient = patient, Plans = plans, Reports = reports,
            Devices = devices.ToDictionary(d => d.Id, d => new DeviceEntity { Id=d.Id, DeviceType=d.DeviceType }),
            Templates = templates.ToDictionary(t => t.Id),
            SourceIssues = plans.Where(p => reports.Any(r => r.PlanId == p.Id) &&
                    devices.Any(d => d.Id == p.DeviceId && d.BusinessId != p.BusinessId))
                .Select(p => new ToolIssueDto { Code="IREGO_HISTORICAL_INSTITUTION_CONTEXT", Severity="warning",
                    Scope=$"plan:{p.Id}", Action="display_source_context",
                    Detail="历史报告按患者主键与计划关联；计划机构与设备当前机构不同，以记录机构为准。" }).ToArray(),
            ExecutionEvidencePlanIds = reports.Where(r => r.PlanId.HasValue && !string.IsNullOrWhiteSpace(r.ReportDetails))
                .Select(r => r.PlanId!.Value).ToHashSet(),
            SourceSystem = "project", SourceCompleteness = "authoritative_primary"
        };
    }
}
