namespace AI_WebApi.Model.Entities;
using SqlSugar;
[SugarTable("dbuser")]
public class PatientEntity
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
    public int Id { get; set; }

    public string? Name { get; set; }

    public string? Phone { get; set; }

    public string? UserDetails { get; set; }

    public int? Status { get; set; }

    public bool? IsDeleted { get; set; }

    public int? BusinessId { get; set; }
}
