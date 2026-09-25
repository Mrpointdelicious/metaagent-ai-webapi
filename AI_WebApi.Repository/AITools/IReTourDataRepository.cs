using AI_WebApi.Model.AITools;
using AI_WebApi.Model.Domain.IReGo;
using AI_WebApi.Model.Domain.IReTour;
using AI_WebApi.Model.Entities;
using AI_WebApi.Repository.BASE;

namespace AI_WebApi.Repository.AITools;

/// <summary>
/// Reads only the project-schema IReTour table family. Binding tables are
/// optional governance structures: when missing, direct
/// dbuser.Id = dbwalk.userId remains the only verified binding path and
/// everything else fails closed.
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

        // Direct primary-key binding: dbwalk.userId equals the canonical
        // patient dbuser.Id. Verified by construction, no runtime matching.
        var sourceUserIds = new List<int> { context.PatientId };
        IReTourPatientBinding binding = new()
        {
            Verified = true,
            SourceUserNamespace = "dbuser",
            BindingMethod = "direct_primary_key",
            BindingVersion = 1,
            BusinessId = patient?.BusinessId
        };

        // Optional governance bindings extend the source-user set; a missing
        // table or schema drift must not break the direct binding path.
        try
        {
            var rows = await _bindings.Db.Queryable<IReTourPatientBindingEntity>()
                .Where(x =>
                    x.PatientId == context.PatientId &&
                    x.BindingStatus == "verified")
                .ToListAsync(cancellationToken);
            foreach (var row in rows)
            {
                if (row.SourceSystem == IReTourContract.SourceSystem &&
                    !string.IsNullOrWhiteSpace(row.SourceUserNamespace) &&
                    row.SourceUserId > 0 &&
                    !sourceUserIds.Contains(row.SourceUserId))
                {
                    sourceUserIds.Add(row.SourceUserId);
                }
            }
        }
        catch (Exception)
        {
            // Optional governance table absent or drifted; direct binding remains.
        }

        cancellationToken.ThrowIfCancellationRequested();

        var walks = await _walks.Db.Queryable<IReTourWalkEntity>()
            .Where(x => sourceUserIds.Contains(x.UserId))
            .ToListAsync(cancellationToken);
        if (walks.Count == 0)
        {
            return new IReTourRawDataSet
            {
                Patient = patient,
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
            .ToListAsync(cancellationToken);

        var rawReports = await _rawReports.Db.Queryable<IReTourRawReportEntity>()
            .Where(x => walkIds.Contains(x.PlanId))
            .ToListAsync(cancellationToken);

        var cognitive = await _cognitiveReports.Db
            .Queryable<IReTourCognitiveReportEntity>()
            .Where(x => walkIds.Contains(x.PlanId))
            .ToListAsync(cancellationToken);

        return new IReTourRawDataSet
        {
            Patient = patient,
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
