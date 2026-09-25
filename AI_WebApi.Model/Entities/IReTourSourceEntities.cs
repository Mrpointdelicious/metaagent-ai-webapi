using SqlSugar;

namespace AI_WebApi.Model.Entities;

/// <summary>
/// Physical IReTour source entities in the merged project schema. The optional
/// governance binding tables are read only when they exist; v1 falls back to
/// direct dbuser.Id = dbwalk.userId as the verified binding path.
/// </summary>
[SugarTable("dbwalk")]
public sealed class IReTourWalkEntity
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
    public int Id { get; set; }

    [SugarColumn(ColumnName = "startTime")]
    public string? StartTime { get; set; }

    [SugarColumn(ColumnName = "endTime")]
    public string? EndTime { get; set; }

    [SugarColumn(ColumnName = "parseTime")]
    public string? ParseTime { get; set; }

    [SugarColumn(ColumnName = "createTime")]
    public string? CreateTime { get; set; }

    [SugarColumn(ColumnName = "deviceId")]
    public int DeviceId { get; set; }

    [SugarColumn(ColumnName = "itemId")]
    public int ItemId { get; set; }

    [SugarColumn(ColumnName = "userId")]
    public int UserId { get; set; }

    [SugarColumn(ColumnName = "details")]
    public string? Details { get; set; }

    public int? Status { get; set; }

    [SugarColumn(ColumnName = "userStatus")]
    public int? UserStatus { get; set; }

    [SugarColumn(ColumnName = "gameStatus")]
    public int? GameStatus { get; set; }

    [SugarColumn(ColumnName = "continueTime")]
    public string? ContinueTime { get; set; }

    [SugarColumn(ColumnName = "duration")]
    public int? Duration { get; set; }
}

[SugarTable("walkreportdetails")]
public sealed class IReTourSummaryReportEntity
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
    public int Id { get; set; }

    [SugarColumn(ColumnName = "planId")]
    public int PlanId { get; set; }

    [SugarColumn(ColumnName = "itemId")]
    public int ItemId { get; set; }

    [SugarColumn(ColumnName = "details")]
    public string? Details { get; set; }
}

[SugarTable("walkreport")]
public sealed class IReTourRawReportEntity
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
    public int Id { get; set; }

    [SugarColumn(ColumnName = "planId")]
    public int PlanId { get; set; }

    [SugarColumn(ColumnName = "itemId")]
    public int ItemId { get; set; }
}

[SugarTable("cogwalkreport")]
public sealed class IReTourCognitiveReportEntity
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
    public int Id { get; set; }

    [SugarColumn(ColumnName = "planId")]
    public int PlanId { get; set; }

    [SugarColumn(ColumnName = "itemId")]
    public int ItemId { get; set; }
}

[SugarTable("dbdevice")]
public sealed class IReTourDeviceEntity
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
    public int Id { get; set; }

    [SugarColumn(ColumnName = "DeviceType")]
    public int DeviceType { get; set; }

    [SugarColumn(ColumnName = "BusinessId")]
    public int? BusinessId { get; set; }
}

[SugarTable("dbuser")]
public sealed class IReTourPatientEntity
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
    public int Id { get; set; }

    [SugarColumn(ColumnName = "BusinessId")]
    public int? BusinessId { get; set; }

    [SugarColumn(ColumnName = "Status")]
    public int? Status { get; set; }

    [SugarColumn(ColumnName = "IsDeleted")]
    public int? IsDeleted { get; set; }
}

[SugarTable("iretour_patient_binding")]
public sealed class IReTourPatientBindingEntity
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
    public int Id { get; set; }

    [SugarColumn(ColumnName = "source_system")]
    public string SourceSystem { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "source_user_namespace")]
    public string SourceUserNamespace { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "source_user_id")]
    public int SourceUserId { get; set; }

    [SugarColumn(ColumnName = "patient_id")]
    public int PatientId { get; set; }

    [SugarColumn(ColumnName = "business_id")]
    public int BusinessId { get; set; }

    [SugarColumn(ColumnName = "binding_method")]
    public string BindingMethod { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "binding_status")]
    public string BindingStatus { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "binding_version")]
    public int BindingVersion { get; set; }
}

[SugarTable("iretour_report_binding")]
public sealed class IReTourReportBindingEntity
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
    public int Id { get; set; }

    [SugarColumn(ColumnName = "source_table")]
    public string SourceTable { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "source_report_id")]
    public int SourceReportId { get; set; }

    [SugarColumn(ColumnName = "walk_id")]
    public int WalkId { get; set; }

    [SugarColumn(ColumnName = "link_method")]
    public string LinkMethod { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "link_status")]
    public string LinkStatus { get; set; } = string.Empty;
}
