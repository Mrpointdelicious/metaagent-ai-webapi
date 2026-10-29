using AI_WebApi.Model.Entities;

namespace AI_WebApi.Repository.AITools;

public sealed record PatientTenantMembership(int UserId, int BusinessId, int? Enable);
public sealed record PatientRoleBinding(int UserId, int? BusinessId, int? RoleId, int? Enable);
public sealed class PatientAccessData
{
    public IReadOnlyList<PatientEntity> Accounts { get; init; } = Array.Empty<PatientEntity>();
    public IReadOnlyList<PatientTenantMembership> Memberships { get; init; } = Array.Empty<PatientTenantMembership>();
    public IReadOnlyList<PatientRoleBinding> Roles { get; init; } = Array.Empty<PatientRoleBinding>();
    public IReadOnlyDictionary<int, string?> Institutions { get; init; } = new Dictionary<int, string?>();
}

public interface IPatientAccessRepository
{
    Task<IReadOnlyList<PatientEntity>> FindAnchorAsync(string kind, string value, CancellationToken ct);
    Task<PatientAccessData> LoadRelatedAsync(PatientEntity anchor, IReadOnlyList<int> confirmedIds, CancellationToken ct);
}
