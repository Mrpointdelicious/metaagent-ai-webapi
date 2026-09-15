using AI_WebApi.Model.Entities;
using AI_WebApi.Repository.UnitOfWorks;
using SqlSugar;

namespace AI_WebApi.Repository.AITools;

/// <summary>
/// Resolves current project patients. Historical method/DTO names retain the
/// public contract; no old IDs, name matching or cross-database mapping.
/// </summary>
public sealed class PatientIdentityMappingRepository : IPatientIdentityMappingRepository
{
    private readonly SqlSugarScope _db;

    public PatientIdentityMappingRepository(IUnitOfWorkManage unitOfWorkManage)
    {
        _db = unitOfWorkManage.GetDbClient() ??
            throw new InvalidOperationException("SqlSugarScope is not available.");
    }

    private ISugarQueryable<PatientEntity> Patients() => _db.Queryable<PatientEntity>()
        .Where(x => (x.IsDeleted == null || x.IsDeleted == false) &&
            SqlFunc.Subqueryable<DbRoleUserEntity>().Where(r =>
                r.UserId == x.Id && r.RoleId == DbRoleUserEntity.PatientRole).Any());

    public async Task<IReadOnlyList<int>> FindRobotPatientCandidatesAsync(
        int projectPatientId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var ids = await Patients().Where(x => x.Id == projectPatientId)
            .Select(x => x.Id).ToListAsync();
        cancellationToken.ThrowIfCancellationRequested();
        return ids;
    }

    public async Task<IReadOnlyList<PatientIdentityMappingCandidate>> FindRobotPatientCandidatesByPhoneAsync(
        string phone, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(phone)) return Array.Empty<PatientIdentityMappingCandidate>();
        var ids = await Patients().Where(x => x.Phone == phone)
            .OrderBy(x => x.Id).Select(x => x.Id).Take(2).ToListAsync();
        cancellationToken.ThrowIfCancellationRequested();
        return ids.Select(id => new PatientIdentityMappingCandidate(id, id)).ToArray();
    }
}
