using AI_WebApi.Model.Domain.IReGo;
using AI_WebApi.Model.Entities;
using AI_WebApi.Repository.UnitOfWorks;
using SqlSugar;

namespace AI_WebApi.Repository.AITools;

public sealed class AssessmentSource
{
    public IReTourPatientEntity? Patient { get; init; }
    public IReadOnlyList<RehabPlanEntity> Plans { get; init; } = Array.Empty<RehabPlanEntity>();
    public IReadOnlyList<RehabReportEntity> Reports { get; init; } = Array.Empty<RehabReportEntity>();
}

public interface IAssessmentDataRepository
{
    Task<AssessmentSource> LoadAsync(TrustedPatientContext context, CancellationToken cancellationToken);
}

/// <summary>SELECT-only online plans; excludes physical devices including IReCog.</summary>
public sealed class AssessmentDataRepository : IAssessmentDataRepository
{
    private readonly SqlSugarScopeProvider _db;
    public AssessmentDataRepository(IUnitOfWorkManage unit) => _db = unit.GetDbClient().GetConnectionScope("project_main");

    public async Task<AssessmentSource> LoadAsync(TrustedPatientContext context, CancellationToken cancellationToken)
    {
        var access = context.RequireAccess();
        var patient = await _db.Queryable<IReTourPatientEntity>()
            .Where(p => p.Id == access.AnchorUserId && p.BusinessId == 0 && p.Status == 1 && p.IsDeleted == 0).FirstAsync(cancellationToken);
        if (patient == null) return new();
        var plans = await _db.Queryable<RehabPlanEntity>()
            .Where(p => p.UserId == access.AnchorUserId &&
                p.DeviceId == -1 && (p.TemplateId == 0 || p.TemplateId == -1))
            .WhereIF(access.DataScope == "current_tenant", p => p.BusinessId == access.CurrentBusinessId)
            .OrderBy(p => p.Id, SqlSugar.OrderByType.Desc).Take(1001).ToListAsync(cancellationToken);
        if (plans.Count > 1000) throw new AssessmentReadLimitException();
        foreach (var plan in plans)
        {
            plan.RecordTenant = access.Observe($"project.dbrehaplan:{plan.Id}", plan.BusinessId, "project.dbrehaplan.BusinessId", plan.UserId);
            plan.OwnerUserRef = access.Reference("owner", "project.dbuser|" + plan.UserId);
        }
        var ids = plans.Select(p => p.Id).ToArray();
        var reports = ids.Length == 0 ? new List<RehabReportEntity>() : await _db.Queryable<RehabReportEntity>()
            .Where(r => ids.Contains(r.PlanId ?? 0)).Take(5001).ToListAsync(cancellationToken);
        if (reports.Count > 5000) throw new AssessmentReadLimitException();
        return new() { Patient = patient, Plans = plans, Reports = reports };
    }
}

public sealed class AssessmentReadLimitException : Exception { }
