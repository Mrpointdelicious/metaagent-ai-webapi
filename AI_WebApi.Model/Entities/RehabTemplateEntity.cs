namespace AI_WebApi.Model.Entities;
using SqlSugar;

[SugarTable("dbtemplates")]
public class RehabTemplateEntity
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
    public int Id { get; set; }

    public string? Title { get; set; }
    public string? Details { get; set; }
    public int? Duration { get; set; }
    public int? ModelId { get; set; }
}
