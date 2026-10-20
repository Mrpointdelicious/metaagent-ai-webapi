using AI_WebApi.Model.Domain.IReGo;
using AI_WebApi.Model.Entities;
using AI_WebApi.Repository.BASE;

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

/// <summary>SELECT-only project adapter. DeviceType=5; patient, plan and device institution must agree.</summary>
public sealed class IReCogDataRepository : IIReCogDataRepository
{
    private readonly IBaseRepository<IReTourPatientEntity> _patients;
    public IReCogDataRepository(IBaseRepository<IReTourPatientEntity> patients) { _patients = patients; }
    public async Task<IReCogSource> LoadAsync(TrustedPatientContext context, CancellationToken cancellationToken)
    {
        var db = _patients.Db;
        var patient = await db.Queryable<IReTourPatientEntity>()
            .Where(x => x.Id == context.PatientId && x.Status == 1 && x.IsDeleted == 0).FirstAsync(cancellationToken);
        if (patient?.BusinessId is not > 0) return new();
        var plans = await db.Queryable<RehabPlanEntity>()
            .InnerJoin<IReTourDeviceEntity>((p, d) => p.DeviceId == d.Id)
            .Where((p, d) => p.UserId == context.PatientId && p.BusinessId == patient.BusinessId &&
                d.BusinessId == patient.BusinessId && d.DeviceType == 5)
            .OrderBy((p, d) => p.Id, SqlSugar.OrderByType.Desc).Select((p, d) => p)
            .Take(1001).ToListAsync(cancellationToken);
        if (plans.Count > 1000) throw new IReCogReadLimitException();
        var ids = plans.Select(p => p.Id).ToArray();
        var reports = ids.Length == 0 ? new List<RehabReportEntity>() : await db.Queryable<RehabReportEntity>()
            .Where(r => ids.Contains(r.PlanId ?? 0)).Take(5001).ToListAsync(cancellationToken);
        if (reports.Count > 5000) throw new IReCogReadLimitException();
        return new() { Patient = patient, Plans = plans, Reports = reports };
    }
}

public sealed class IReCogReadLimitException : Exception { }
