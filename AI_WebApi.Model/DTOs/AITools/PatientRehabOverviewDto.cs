namespace AI_WebApi.Model.DTOs.AITools;

public class PatientRehabOverviewDto
{
    public DateTimeOffset GeneratedAt { get; set; }

    public string TimeZone { get; set; } = "Asia/Shanghai";

    public PatientSummaryDto Patient { get; set; } = new();

    public PastTrainingSummaryDto PastTraining { get; set; } = new();

    public FuturePlanSummaryDto FuturePlans { get; set; } = new();

    public DataQualityDto DataQuality { get; set; } = new();
}

public class PatientSummaryDto
{
    public int PatientId { get; set; }

    public string DisplayName { get; set; } = string.Empty;

    public string? Sex { get; set; }

    public int? Age { get; set; }

    public decimal? HeightCm { get; set; }

    public decimal? WeightKg { get; set; }

    public string? DiagnosisSummary { get; set; }
}

public class PastTrainingSummaryDto
{
    public DateTimeOffset WindowStart { get; set; }

    public DateTimeOffset WindowEnd { get; set; }

    public bool HasRecords { get; set; }

    public int RecordCount { get; set; }

    public decimal? TotalDurationMinutes { get; set; }

    public int? TotalTaskCount { get; set; }

    public string? EmptyMessage { get; set; }

    public List<TrainingRecordDto> Records { get; set; } = new();
}

public class TrainingRecordDto
{
    public int ReportId { get; set; }

    public int PlanId { get; set; }

    public DateTimeOffset TrainingTime { get; set; }

    public string DeviceType { get; set; } = "iReGo";

    public decimal? DurationMinutes { get; set; }

    public int? TaskCount { get; set; }

    public decimal? WalkingDistanceMeters { get; set; }

    public int? SitToStandCount { get; set; }

    public List<string> TaskNames { get; set; } = new();

    public List<string> DataQualityNotes { get; set; } = new();
}
public class FuturePlanSummaryDto
{
    public DateTimeOffset WindowStart { get; set; }

    public DateTimeOffset WindowEnd { get; set; }

    public bool HasPlans { get; set; }

    public int PlanCount { get; set; }

    public string? EmptyMessage { get; set; }

    public List<FuturePlanDto> Plans { get; set; } = new();
}

public class FuturePlanDto
{
    public int PlanId { get; set; }

    public DateTimeOffset ScheduledAt { get; set; }

    public string DeviceType { get; set; } = "iReGo";

    public string? TemplateTitle { get; set; }

    public decimal? PlannedDurationMinutes { get; set; }

    public List<string> TaskNames { get; set; } = new();

    public string SourceNote { get; set; } = string.Empty;
}

public class DataQualityDto
{
    public string Level { get; set; } = "high";

    public List<string> Notes { get; set; } = new();
}