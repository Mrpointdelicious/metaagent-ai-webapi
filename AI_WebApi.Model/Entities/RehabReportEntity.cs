namespace AI_WebApi.Model.Entities;
using SqlSugar;
[SugarTable("dbreport")]
public class RehabReportEntity
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
    public int Id { get; set; }

    [SugarColumn(ColumnName = "planId")]
    public int? PlanId { get; set; }

    public string? ReportDetails { get; set; }
    public DateTime? CreateTime { get; set; }
    public DateTime? UpdateTime { get; set; }
}