using AI_WebApi.Model.Domain.IReGo;
using AI_WebApi.Model.Entities;
using AI_WebApi.Repository.BASE;

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
    private readonly IBaseRepository<IReTourPatientEntity> _patients;
    public AssessmentDataRepository(IBaseRepository<IReTourPatientEntity> patients) => _patients = patients;

    public async Task<AssessmentSource> LoadAsync(TrustedPatientContext context, CancellationToken cancellationToken)
    {
        var db = _patients.Db;
        var patient = await db.Queryable<IReTourPatientEntity>()
            .Where(p => p.Id == context.PatientId && p.Status == 1 && p.IsDeleted == 0).FirstAsync(cancellationToken);
        if (patient == null || context.Access == null && patient.BusinessId is not > 0) return new();
        var owners = context.Access?.UserIds ?? new[] { context.PatientId };
        var plans = await db.Queryable<RehabPlanEntity>()
            .Where(p => owners.Contains(p.UserId ?? 0) &&
                p.DeviceId == -1 && (p.TemplateId == 0 || p.TemplateId == -1))
            .WhereIF(context.Access == null, p => p.BusinessId == patient.BusinessId)
            .WhereIF(context.Access?.DataScope == "current_tenant", p => p.BusinessId == context.Access!.CurrentBusinessId)
            .OrderBy(p => p.Id, SqlSugar.OrderByType.Desc).Take(1001).ToListAsync(cancellationToken);
        if (plans.Count > 1000) throw new AssessmentReadLimitException();
        if (context.Access != null)
            foreach (var plan in plans)
            {
                plan.RecordTenant = context.Access.Observe($"project.dbrehaplan:{plan.Id}", plan.BusinessId, "project.dbrehaplan.BusinessId", plan.UserId);
                plan.OwnerUserRef = context.Access.Reference("owner", "project.dbuser|" + plan.UserId);
            }
        var ids = plans.Select(p => p.Id).ToArray();
        var reports = ids.Length == 0 ? new List<RehabReportEntity>() : await db.Queryable<RehabReportEntity>()
            .Where(r => ids.Contains(r.PlanId ?? 0)).Take(5001).ToListAsync(cancellationToken);
        if (reports.Count > 5000) throw new AssessmentReadLimitException();
        return new() { Patient = patient, Plans = plans, Reports = reports };
    }
}

public sealed class AssessmentReadLimitException : Exception { }
