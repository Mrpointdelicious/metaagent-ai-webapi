using System.Globalization;
using AI_WebApi.Model.Entities;
using AI_WebApi.Repository.UnitOfWorks;
using SqlSugar;

namespace AI_WebApi.Repository.AITools;

public sealed class PatientAccessRepository : IPatientAccessRepository
{
    private readonly SqlSugarScopeProvider _db;
    public PatientAccessRepository(IUnitOfWorkManage unit) => _db = unit.GetDbClient().GetConnectionScope("project_main");

    public async Task<IReadOnlyList<PatientEntity>> FindAnchorAsync(string kind, string value, CancellationToken ct)
    {
        var query = _db.Queryable<PatientEntity>();
        if (kind == "project_user_id" && int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0)
            query = query.Where(u => u.Id == id);
        else if (kind == "login_name") query = query.Where(u => u.LoginName == value);
        else return Array.Empty<PatientEntity>();
        return await query.Select(u => new PatientEntity
            { Id = u.Id, Status = u.Status, IsDeleted = u.IsDeleted, BusinessId = u.BusinessId }).Take(2).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<PatientEntity>> FindAnchorByPhoneAsync(string phone, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(phone)) return Array.Empty<PatientEntity>();
        return await _db.Queryable<PatientEntity>()
            .Where(u => u.Phone == phone && u.BusinessId == 0 && u.Status == 1 && u.IsDeleted == false)
            .Select(u => new PatientEntity
                { Id = u.Id, Status = u.Status, IsDeleted = u.IsDeleted, BusinessId = u.BusinessId })
            .Take(2).ToListAsync(ct);
    }

    public async Task<PatientAccessData> LoadAccessAsync(int userId, CancellationToken ct)
    {
        var memberships = await _db.Queryable<PatientTenantEntity>().Where(t => t.UserId == userId).ToListAsync(ct);
        var roles = await _db.Queryable<DbRoleUserEntity>().Where(r => r.UserId == userId).ToListAsync(ct);
        var institutions = await _db.Queryable<PatientInstitutionEntity>().Take(10001).ToListAsync(ct);
        if (institutions.Count > 10000) throw new InvalidOperationException("INSTITUTION_DIRECTORY_READ_LIMIT");
        return new()
        {
            Memberships = memberships.Select(t => new PatientTenantMembership(t.UserId, t.BusinessId, t.Enable)).Distinct().ToArray(),
            Roles = roles.Select(r => new PatientRoleBinding(r.UserId ?? 0, r.BusinessId, r.RoleId, r.Enable)).Distinct().ToArray(),
            Institutions = institutions.ToDictionary(t => t.Id, t => t.Name)
        };
    }
}
