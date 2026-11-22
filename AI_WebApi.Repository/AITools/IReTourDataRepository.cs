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
        access.AddIssue("SOURCE_PATIENT_BINDING_UNVERIFIED");
        if (access.DataScope == "current_tenant")
            access.AddIssue("IRETOUR_HISTORICAL_TENANT_COVERAGE_PARTIAL");
        var confirmedWalkIds = access.WalkRecords.Keys.ToArray();
        if (confirmedWalkIds.Length == 0) return new() { Patient = patient };
        var walks = await _db.Queryable<IReTourWalkEntity>()
            .InnerJoin<IReTourDeviceEntity>((w, d) => w.DeviceId == d.Id)
            .Where((w, d) => d.DeviceType == IReTourContract.RequiredDeviceType && confirmedWalkIds.Contains(w.Id))
            .OrderBy((w, d) => w.Id, SqlSugar.OrderByType.Desc)
            .Select((w, d) => w).Take(1001).ToListAsync(cancellationToken);
        if (walks.Count > 1000) throw new IReTourReadLimitException();
        walks.RemoveAll(walk => walk.BusinessId > 0 && access.WalkRecords[walk.Id].BusinessId is > 0 &&
            walk.BusinessId != access.WalkRecords[walk.Id].BusinessId);
        if (walks.Count == 0) return new() { Patient = patient };
        foreach (var walk in walks)
        {
            var binding = access.WalkRecords[walk.Id];
            walk.VerifiedBusinessId = walk.BusinessId > 0 ? walk.BusinessId : binding.BusinessId;
            walk.RecordTenant = access.Observe($"project.dbwalk:{walk.Id}", walk.VerifiedBusinessId,
                walk.BusinessId > 0 ? "project.dbwalk.BusinessId" : "confirmed_walk_record_binding", binding.ProjectUserId);
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
            Binding = new IReTourPatientBinding { Verified = true, SourceUserNamespace = "project.dbuser", BindingMethod = "confirmed_walk_record", BindingVersion = 1 },
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
