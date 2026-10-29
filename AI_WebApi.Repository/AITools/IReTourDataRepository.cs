using AI_WebApi.Model.AITools;
using AI_WebApi.Model.Domain.IReGo;
using AI_WebApi.Model.Domain.IReTour;
using AI_WebApi.Model.Entities;
using AI_WebApi.Repository.BASE;

namespace AI_WebApi.Repository.AITools;

/// <summary>
/// Reads only the project-schema IReTour table family. Requires direct
/// dbuser.Id = dbwalk.userId and a DeviceType=6 device in the same institution.
/// Patient aliases are not expanded by this production read adapter.
/// </summary>
public sealed class IReTourDataRepository : IIReTourDataRepository
{
    private readonly IBaseRepository<IReTourPatientEntity> _patients;
    private readonly IBaseRepository<IReTourWalkEntity> _walks;
    private readonly IBaseRepository<IReTourDeviceEntity> _devices;
    private readonly IBaseRepository<IReTourSummaryReportEntity> _summaries;
    private readonly IBaseRepository<IReTourRawReportEntity> _rawReports;
    private readonly IBaseRepository<IReTourCognitiveReportEntity> _cognitiveReports;
    private readonly IBaseRepository<IReTourPatientBindingEntity> _bindings;

    public IReTourDataRepository(
        IBaseRepository<IReTourPatientEntity> patients,
        IBaseRepository<IReTourWalkEntity> walks,
        IBaseRepository<IReTourDeviceEntity> devices,
        IBaseRepository<IReTourSummaryReportEntity> summaries,
        IBaseRepository<IReTourRawReportEntity> rawReports,
        IBaseRepository<IReTourCognitiveReportEntity> cognitiveReports,
        IBaseRepository<IReTourPatientBindingEntity> bindings)
    {
        _patients = patients;
        _walks = walks;
        _devices = devices;
        _summaries = summaries;
        _rawReports = rawReports;
        _cognitiveReports = cognitiveReports;
        _bindings = bindings;
    }

    public async Task<IReTourRawDataSet> LoadPatientDataAsync(
        TrustedPatientContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var patient = await _patients.Db.Queryable<IReTourPatientEntity>()
            .Where(x => x.Id == context.PatientId)
            .FirstAsync(cancellationToken);

        var access = context.Access;
        if (patient == null || patient.Status != 1 || patient.IsDeleted != 0 || access == null && patient.BusinessId is not > 0)
            return new IReTourRawDataSet();

        // Production path uses only canonical project IDs. No runtime phone/name or optional alias expansion.
        var sourceUserIds = access?.UserIds.ToList() ?? new List<int> { context.PatientId };
        var binding = new IReTourPatientBinding
        {
            Verified = true, SourceUserNamespace = "dbuser", BindingMethod = "direct_primary_key",
            BindingVersion = 1, BusinessId = patient.BusinessId
        };
        var owners = sourceUserIds.ToArray();
        var confirmedWalkIds = access?.WalkRecords.Keys.ToArray() ?? Array.Empty<int>();
        if (access != null && !access.WalkUserIdsUseProjectNamespace)
            access.AddIssue("SOURCE_PATIENT_BINDING_UNVERIFIED");
        if (access?.DataScope == "current_tenant")
        {
            access.AddIssue("IRETOUR_HISTORICAL_TENANT_COVERAGE_PARTIAL");
            confirmedWalkIds = access.WalkRecords.Values.Where(w => w.BusinessId == access.CurrentBusinessId).Select(w => w.WalkId).ToArray();
        }
        var namespaceVerified = access?.WalkUserIdsUseProjectNamespace == true && access.DataScope == "all_tenants";
        var walks = await _walks.Db.Queryable<IReTourWalkEntity>()
            .InnerJoin<IReTourDeviceEntity>((w, d) => w.DeviceId == d.Id)
            .Where((w, d) => d.DeviceType == IReTourContract.RequiredDeviceType)
            .WhereIF(access == null, (w, d) => w.UserId == context.PatientId && d.BusinessId == patient.BusinessId)
            .WhereIF(access != null, (w, d) => confirmedWalkIds.Contains(w.Id) || namespaceVerified && owners.Contains(w.UserId))
            .OrderBy((w, d) => w.Id, SqlSugar.OrderByType.Desc)
            .Select((w, d) => w).Take(1001).ToListAsync(cancellationToken);
        if (walks.Count > 1000) throw new IReTourReadLimitException();
        if (access != null)
            foreach (var walk in walks)
            {
                access.WalkRecords.TryGetValue(walk.Id, out var verified);
                walk.VerifiedBusinessId = verified?.BusinessId;
                walk.RecordTenant = access.Observe($"project.dbwalk:{walk.Id}", verified?.BusinessId,
                    verified == null ? "unverified_historical_tenant" : "confirmed_walk_record_binding", verified?.ProjectUserId ?? walk.UserId);
            }
        if (walks.Count == 0)
        {
            return new IReTourRawDataSet
            {
                Patient = patient,
                ScopedOwnershipVerified = access != null,
                Binding = binding,
                SourceUserIds = sourceUserIds
            };
        }

        var walkIds = walks.Select(x => x.Id).ToList();
        var deviceIds = walks.Select(x => x.DeviceId).Distinct().ToList();

        var devices = await _devices.Db.Queryable<IReTourDeviceEntity>()
            .Where(x => deviceIds.Contains(x.Id))
            .ToListAsync(cancellationToken);

        var summaries = await _summaries.Db.Queryable<IReTourSummaryReportEntity>()
            .Where(x => walkIds.Contains(x.PlanId))
            .Take(5001).ToListAsync(cancellationToken);

        var rawReports = await _rawReports.Db.Queryable<IReTourRawReportEntity>()
            .Where(x => walkIds.Contains(x.PlanId))
            .Take(5001).ToListAsync(cancellationToken);

        var cognitive = await _cognitiveReports.Db
            .Queryable<IReTourCognitiveReportEntity>()
            .Where(x => walkIds.Contains(x.PlanId))
            .Take(5001).ToListAsync(cancellationToken);

        if (summaries.Count > 5000 || rawReports.Count > 5000 || cognitive.Count > 5000) throw new IReTourReadLimitException();
        return new IReTourRawDataSet
        {
            Patient = patient,
            ScopedOwnershipVerified = access != null,
            Binding = binding,
            SourceUserIds = sourceUserIds,
            Walks = walks,
            Devices = devices.ToDictionary(x => x.Id),
            SummaryReports = summaries,
            RawReports = rawReports,
            CognitiveReports = cognitive
        };
    }
}

public sealed class IReTourReadLimitException : Exception { }
