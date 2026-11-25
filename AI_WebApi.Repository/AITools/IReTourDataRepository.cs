using AI_WebApi.Model.AITools;
using AI_WebApi.Model.Domain.IReGo;
using AI_WebApi.Model.Domain.IReTour;
using AI_WebApi.Model.Entities;
using AI_WebApi.Repository.UnitOfWorks;
using SqlSugar;

namespace AI_WebApi.Repository.AITools;

public sealed class IReTourDataRepository : IIReTourDataRepository
{
    private readonly SqlSugarScopeProvider _db;
    public IReTourDataRepository(IUnitOfWorkManage unit) => _db = unit.GetDbClient().GetConnectionScope("project_main");

    public async Task<IReTourRawDataSet> LoadPatientDataAsync(
        TrustedPatientContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var access = context.RequireAccess();
        var patient = await _db.Queryable<IReTourPatientEntity>()
            .Where(x => x.Id == access.AnchorUserId && x.BusinessId == 0 && x.Status == 1 && x.IsDeleted == 0)
            .FirstAsync(cancellationToken);
        if (patient == null) return new();
        var binding = new IReTourPatientBinding
        {
            Verified = true,
            SourceUserNamespace = "project.dbuser",
            BindingMethod = "direct_primary_key",
            BindingVersion = 1
        };
        var walks = await _db.Queryable<IReTourWalkEntity>()
            .InnerJoin<IReTourDeviceEntity>((w, d) => w.DeviceId == d.Id)
            .Where((w, d) => w.UserId == access.AnchorUserId && d.DeviceType == IReTourContract.RequiredDeviceType)
            .WhereIF(access.DataScope == "current_tenant", (w, d) => w.BusinessId == access.CurrentBusinessId)
            .OrderBy((w, d) => w.Id, SqlSugar.OrderByType.Desc)
            .Select((w, d) => w).Take(1001).ToListAsync(cancellationToken);
        if (walks.Count > 1000) throw new IReTourReadLimitException();
        if (walks.Count == 0)
        {
            return new IReTourRawDataSet
            {
                Patient = patient,
                ScopedOwnershipVerified = true,
                Binding = binding,
                SourceUserIds = new[] { access.AnchorUserId }
            };
        }
        foreach (var walk in walks)
        {
            walk.VerifiedBusinessId = walk.BusinessId > 0 ? walk.BusinessId : null;
            walk.RecordTenant = access.Observe($"project.dbwalk:{walk.Id}", walk.VerifiedBusinessId,
                "project.dbwalk.BusinessId", walk.UserId);
        }

        var walkIds = walks.Select(x => x.Id).ToList();
        var deviceIds = walks.Select(x => x.DeviceId).Distinct().ToList();

        var devices = await _db.Queryable<IReTourDeviceEntity>()
            .Where(x => deviceIds.Contains(x.Id))
            .ToListAsync(cancellationToken);

        var summaries = await _db.Queryable<IReTourSummaryReportEntity>()
            .Where(x => walkIds.Contains(x.PlanId))
            .Take(5001).ToListAsync(cancellationToken);

        var rawReports = await _db.Queryable<IReTourRawReportEntity>()
            .Where(x => walkIds.Contains(x.PlanId))
            .Take(5001).ToListAsync(cancellationToken);

        var cognitive = await _db
            .Queryable<IReTourCognitiveReportEntity>()
            .Where(x => walkIds.Contains(x.PlanId))
            .Take(5001).ToListAsync(cancellationToken);

        if (summaries.Count > 5000 || rawReports.Count > 5000 || cognitive.Count > 5000) throw new IReTourReadLimitException();
        return new IReTourRawDataSet
        {
            Patient = patient,
            ScopedOwnershipVerified = true,
            Binding = binding,
            SourceUserIds = new[] { access.AnchorUserId },
            Walks = walks,
            Devices = devices.ToDictionary(x => x.Id),
            SummaryReports = summaries,
            RawReports = rawReports,
            CognitiveReports = cognitive
        };
    }
}

public sealed class IReTourReadLimitException : Exception { }
