namespace AI_WebApi.Model.Entities;
using SqlSugar;
[SugarTable("dbdevice1")]
public class LegacyDeviceEntity
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
    public int Id { get; set; }

    public int? DeviceType { get; set; }
}