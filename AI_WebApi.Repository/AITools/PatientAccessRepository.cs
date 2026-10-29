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
        return await query.Take(2).ToListAsync(ct);
    }

    public async Task<PatientAccessData> LoadRelatedAsync(PatientEntity anchor, IReadOnlyList<int> confirmedIds, CancellationToken ct)
    {
        var ids = confirmedIds.Append(anchor.Id).Distinct().ToArray();
        var phone = anchor.Phone?.Trim();
        var accounts = await _db.Queryable<PatientEntity>()
            .Where(u => ids.Contains(u.Id) || phone != null && phone != "" && u.Phone == phone)
            .Take(101).ToListAsync(ct);
        if (accounts.Count > 100) throw new InvalidOperationException("PATIENT_IDENTITY_READ_LIMIT");
        var accountIds = accounts.Select(u => u.Id).ToArray();
        var memberships = await _db.Queryable<PatientTenantEntity>().Where(t => accountIds.Contains(t.UserId)).ToListAsync(ct);
        var roles = await _db.Queryable<DbRoleUserEntity>().Where(r => accountIds.Contains(r.UserId ?? 0)).ToListAsync(ct);
        var institutions = await _db.Queryable<PatientInstitutionEntity>().Take(10001).ToListAsync(ct);
        if (institutions.Count > 10000) throw new InvalidOperationException("INSTITUTION_DIRECTORY_READ_LIMIT");
        return new()
        {
            Accounts = accounts,
            Memberships = memberships.Select(t => new PatientTenantMembership(t.UserId, t.BusinessId, t.Enable)).Distinct().ToArray(),
            Roles = roles.Select(r => new PatientRoleBinding(r.UserId ?? 0, r.BusinessId, r.RoleId, r.Enable)).Distinct().ToArray(),
            Institutions = institutions.ToDictionary(t => t.Id, t => t.Name)
        };
    }
}
