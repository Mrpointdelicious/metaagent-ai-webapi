using AI_WebApi.Model.Entities;
using AI_WebApi.Repository.UnitOfWorks;
using SqlSugar;

namespace AI_WebApi.Repository.AITools;

/// <summary>
/// Temporary two-database identity adapter. Remove it after the databases are
/// merged, while retaining the public service contract used by Dify.
/// </summary>
public sealed class PatientIdentityMappingRepository
    : IPatientIdentityMappingRepository
{
    public const string ProjectConnectionId = "project_identity_temp";
    public const string RobotConnectionId = "robotdb_main";
    public const int PatientUserType = 2;

    private readonly SqlSugarScope _db;

    public PatientIdentityMappingRepository(IUnitOfWorkManage unitOfWorkManage)
    {
        _db = unitOfWorkManage.GetDbClient() ??
            throw new InvalidOperationException(
                "SqlSugarScope is not available.");
    }

    public async Task<IReadOnlyList<int>> FindRobotPatientCandidatesAsync(
        int projectPatientId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var projectDb = _db.GetConnectionScope(ProjectConnectionId);
        var source = await projectDb.Queryable<PatientEntity>()
            .Where(x => x.Id == projectPatientId)
            .Select(x => new PatientIdentitySourceRow
            {
                Name = x.Name
            })
            .FirstAsync();

        cancellationToken.ThrowIfCancellationRequested();
        var patientName = source?.Name?.Trim();
        if (string.IsNullOrWhiteSpace(patientName))
        {
            return Array.Empty<int>();
        }

        var robotDb = _db.GetConnectionScope(RobotConnectionId);
        var candidateIds = await robotDb
            .Queryable<RobotDbMainUserEntity>()
            .Where(x =>
                x.Name == patientName &&
                x.UserType == PatientUserType)
            .OrderBy(x => x.Id)
            .Select(x => x.Id)
            .Take(2)
            .ToListAsync();

        cancellationToken.ThrowIfCancellationRequested();
        return candidateIds;
    }

    public async Task<IReadOnlyList<PatientIdentityMappingCandidate>>
        FindRobotPatientCandidatesByPhoneAsync(
            string phone,
            CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var projectDb = _db.GetConnectionScope(ProjectConnectionId);
        var sources = await projectDb.Queryable<PatientEntity>()
            .Where(x => x.Phone == phone)
            .OrderBy(x => x.Id)
            .Select(x => new PatientIdentitySourceRow
            {
                ProjectPatientId = x.Id,
                Name = x.Name
            })
            .Take(2)
            .ToListAsync();

        cancellationToken.ThrowIfCancellationRequested();
        if (sources.Count != 1)
        {
            return Array.Empty<PatientIdentityMappingCandidate>();
        }

        var source = sources[0];
        var patientName = source.Name?.Trim();
        if (string.IsNullOrWhiteSpace(patientName))
        {
            return Array.Empty<PatientIdentityMappingCandidate>();
        }

        var robotDb = _db.GetConnectionScope(RobotConnectionId);
        var robotPatientIds = await robotDb
            .Queryable<RobotDbMainUserEntity>()
            .Where(x =>
                x.Name == patientName &&
                x.UserType == PatientUserType)
            .OrderBy(x => x.Id)
            .Select(x => x.Id)
            .Take(2)
            .ToListAsync();

        cancellationToken.ThrowIfCancellationRequested();
        return robotPatientIds
            .Select(x => new PatientIdentityMappingCandidate(
                source.ProjectPatientId,
                x))
            .ToArray();
    }

    private sealed class PatientIdentitySourceRow
    {
        public int ProjectPatientId { get; init; }
        public string? Name { get; init; }
    }
}
