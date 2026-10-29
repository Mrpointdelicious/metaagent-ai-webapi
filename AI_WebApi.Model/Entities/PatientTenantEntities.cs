using SqlSugar;

namespace AI_WebApi.Model.Entities;

[SugarTable("dbbuniessuser")]
public sealed class PatientTenantEntity
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)] public int Id { get; set; }
    public int UserId { get; set; }
    public int BusinessId { get; set; }
    public int? Enable { get; set; }
}

[SugarTable("dbbuniess")]
public sealed class PatientInstitutionEntity
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)] public int Id { get; set; }
    public string? Name { get; set; }
}
