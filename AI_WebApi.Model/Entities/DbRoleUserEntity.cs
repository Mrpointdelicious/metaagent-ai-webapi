using SqlSugar;

namespace AI_WebApi.Model.Entities;

[SugarTable("dbroleuser")]
public sealed class DbRoleUserEntity
{
    public const int PatientRoleId = 0;
    public const int StudentRoleId = 10;

    public int? UserId { get; set; }
    public int? RoleId { get; set; }
    public int? BusinessId { get; set; }
    public int? Enable { get; set; }
}
