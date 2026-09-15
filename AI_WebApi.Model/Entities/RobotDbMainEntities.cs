using SqlSugar;

namespace AI_WebApi.Model.Entities;

/// <summary>
/// Physical project entities used by the IREGO adapter.
/// Historical class names are retained to keep the adapter changes local.
/// </summary>
[SugarTable("dbuser")]
public sealed class RobotDbMainUserEntity
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
    public int Id { get; set; }

    public int? BusinessId { get; set; }
    public bool? IsDeleted { get; set; }
    public string? Name { get; set; }
    public int? Status { get; set; }

    public string? UserDetails { get; set; }
}

[SugarTable("rehabplan")]
public sealed class RobotDbMainRehabPlanEntity
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
    public int Id { get; set; }

    [SugarColumn(ColumnName = "doctorid")]
    public int DoctorId { get; set; }

    public int PatientId { get; set; }

    [SugarColumn(ColumnName = "devicetype")]
    public int DeviceType { get; set; }

    public DateTime StartTime { get; set; }
    public int? CompleteStatus { get; set; }
    public string? Details { get; set; }

    [SugarColumn(ColumnName = "createtime")]
    public DateTime? CreateTime { get; set; }

    [SugarColumn(ColumnName = "templateid")]
    public int TemplateId { get; set; }

}

[SugarTable("report")]
public sealed class RobotDbMainReportEntity
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = false)]
    public int Id { get; set; }

    public string? Details { get; set; }
    public DateTime? ReportTime { get; set; }
    public int Status { get; set; }
}

[SugarTable("templates")]
public sealed class RobotDbMainTemplateEntity
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
    public int Id { get; set; }

    [SugarColumn(ColumnName = "doctorid")]
    public int DoctorId { get; set; }

    [SugarColumn(ColumnName = "modelid")]
    public int ModelId { get; set; }

    public string? Title { get; set; }
    public string? Details { get; set; }
    public int Status { get; set; }
}

[SugarTable("dbbuniess")]
public sealed class RobotDbMainVendorEntity
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
    public int Id { get; set; }

    public int? Status { get; set; }
}

[SugarTable("reportdata_new")]
public sealed class RobotDbMainReportDataNewEntity
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
    public int Id { get; set; }

    [SugarColumn(ColumnName = "planid")]
    public int PlanId { get; set; }

    public byte[]? Col1 { get; set; }
    public byte[]? Col2 { get; set; }
    public byte[]? Col3 { get; set; }
    public byte[]? Col4 { get; set; }
    public byte[]? Col5 { get; set; }
    public byte[]? Col6 { get; set; }
    public byte[]? Col7 { get; set; }
    public byte[]? Col8 { get; set; }
    public byte[]? Col9 { get; set; }
    public byte[]? Col10 { get; set; }
}

[SugarTable("reportdata")]
public sealed class RobotDbMainLegacyReportDataEntity
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = false)]
    public int Id { get; set; }

    public byte[]? WImponderability { get; set; }
    public byte[]? WLeftSideway { get; set; }
    public byte[]? WRightSideway { get; set; }
    public byte[]? WLeftRoll { get; set; }
    public byte[]? WRightRoll { get; set; }
    public byte[]? WLeftStepSize { get; set; }
    public byte[]? WRightStepSize { get; set; }
    public byte[]? WLeftSpeed { get; set; }
    public byte[]? WRightSpeed { get; set; }
    public byte[]? WTimeStamp { get; set; }
    public byte[]? SImponderability { get; set; }
    public byte[]? SLeftSideway { get; set; }
    public byte[]? SRightSideway { get; set; }
    public byte[]? SSitTime { get; set; }
    public byte[]? SStandTime { get; set; }
    public byte[]? SSitSpeed { get; set; }
    public byte[]? SStandSpeed { get; set; }
    public byte[]? STimeStamp { get; set; }
    public byte[]? GLeftRightMove { get; set; }
    public byte[]? GFrontBackMove { get; set; }
    public byte[]? GTimeStamp { get; set; }
    public byte[]? BImponderability { get; set; }
    public byte[]? BLeftRightMove { get; set; }
    public byte[]? BFrontBackMove { get; set; }
    public byte[]? BXPositions { get; set; }
    public byte[]? BYPositions { get; set; }
    public byte[]? BTimeStamp { get; set; }
}
