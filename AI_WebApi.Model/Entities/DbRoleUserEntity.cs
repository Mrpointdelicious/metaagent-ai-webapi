using SqlSugar;

namespace AI_WebApi.Model.Entities;

[SugarTable("dbroleuser")]
public sealed class DbRoleUserEntity
{
    public const int PatientRole = 0;
    public const int DoctorRole = 1;

    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
    public int Id { get; set; }
    public int? UserId { get; set; }
    public int? RoleId { get; set; }
}
