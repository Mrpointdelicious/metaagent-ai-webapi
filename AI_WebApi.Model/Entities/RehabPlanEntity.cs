namespace AI_WebApi.Model.Entities;
using SqlSugar;
[SugarTable("dbrehaplan")]
public class RehabPlanEntity
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
    public int Id { get; set; }

    public int? UserId { get; set; }
    public int? TemplateId { get; set; }
    public int? BusinessId { get; set; }
    public int? DoctorId { get; set; }

    [SugarColumn(ColumnName = "Deviceid")]
    public int? DeviceId { get; set; }

    public DateTime? CreateTime { get; set; }
    public DateTime? BookingTime { get; set; }
    public DateTime? StartTime { get; set; }

    [SugarColumn(ColumnName = "Endtime")]
    public DateTime? EndTime { get; set; }

    public int? IsComplete { get; set; }
    public int? Duration { get; set; }
    public int? Status { get; set; }
    public string? Details { get; set; }
}