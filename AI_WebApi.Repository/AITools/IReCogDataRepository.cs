using AI_WebApi.Model.Domain.IReGo;
using AI_WebApi.Model.Entities;
using AI_WebApi.Repository.UnitOfWorks;
using SqlSugar;

namespace AI_WebApi.Repository.AITools;

public sealed class IReCogSource
{
    public IReTourPatientEntity? Patient { get; init; }
    public IReadOnlyList<RehabPlanEntity> Plans { get; init; } = Array.Empty<RehabPlanEntity>();
    public IReadOnlyList<RehabReportEntity> Reports { get; init; } = Array.Empty<RehabReportEntity>();
}

public interface IIReCogDataRepository
{
    Task<IReCogSource> LoadAsync(TrustedPatientContext context, CancellationToken cancellationToken);
}

public sealed class IReCogDataRepository : IIReCogDataRepository
{
    private readonly SqlSugarScopeProvider _db;
    public IReCogDataRepository(IUnitOfWorkManage unit) => _db = unit.GetDbClient().GetConnectionScope("project_main");
    public async Task<IReCogSource> LoadAsync(TrustedPatientContext context, CancellationToken cancellationToken)
    {
        var access = context.RequireAccess();
        var patient = await _db.Queryable<IReTourPatientEntity>()
            .Where(x => x.Id == access.AnchorUserId && x.BusinessId == 0 && x.Status == 1 && x.IsDeleted == 0).FirstAsync(cancellationToken);
        if (patient == null) return new();
        var plans = await _db.Queryable<RehabPlanEntity>()
            .InnerJoin<IReTourDeviceEntity>((p, d) => p.DeviceId == d.Id)
            .Where((p, d) => p.UserId == access.AnchorUserId && d.DeviceType == 5)
            .WhereIF(access.DataScope == "current_tenant", (p, d) => p.BusinessId == access.CurrentBusinessId)
            .OrderBy((p, d) => p.Id, SqlSugar.OrderByType.Desc).Select((p, d) => p)
            .Take(1001).ToListAsync(cancellationToken);
        if (plans.Count > 1000) throw new IReCogReadLimitException();
        foreach (var plan in plans)
        {
            plan.RecordTenant = access.Observe($"project.dbrehaplan:{plan.Id}", plan.BusinessId, "project.dbrehaplan.BusinessId", plan.UserId);
            plan.OwnerUserRef = access.Reference("owner", "project.dbuser|" + plan.UserId);
        }
        var ids = plans.Select(p => p.Id).ToArray();
        var reports = ids.Length == 0 ? new List<RehabReportEntity>() : await _db.Queryable<RehabReportEntity>()
            .Where(r => ids.Contains(r.PlanId ?? 0)).Take(5001).ToListAsync(cancellationToken);
        if (reports.Count > 5000) throw new IReCogReadLimitException();
        return new() { Patient = patient, Plans = plans, Reports = reports };
    }
}

public sealed class IReCogReadLimitException : Exception { }
