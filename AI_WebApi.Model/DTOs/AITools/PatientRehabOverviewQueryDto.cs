namespace AI_WebApi.Model.DTOs.AITools;

public class PatientRehabOverviewQueryDto
{
    public int PastDays { get; set; } = 30;

    public int FutureDays { get; set; } = 30;
}