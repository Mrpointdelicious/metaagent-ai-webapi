namespace AI_WebApi.Model.Entities;
using SqlSugar;
[SugarTable("dbdevice")]
public class DeviceEntity
{
    [SugarColumn(
        ColumnName = "id",
        IsPrimaryKey = true,
        IsIdentity = true)]
    public int Id { get; set; }

    public int DeviceType { get; set; }
}