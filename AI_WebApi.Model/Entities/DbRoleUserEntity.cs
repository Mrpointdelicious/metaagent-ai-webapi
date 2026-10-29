using SqlSugar;

namespace AI_WebApi.Model.Entities;

[SugarTable("dbroleuser")]
public sealed class DbRoleUserEntity
{
    public int? UserId { get; set; }
    public int? RoleId { get; set; }
    public int? BusinessId { get; set; }
    public int? Enable { get; set; }
}
