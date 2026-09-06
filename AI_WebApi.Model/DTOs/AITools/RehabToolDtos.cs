using System.ComponentModel.DataAnnotations;

namespace AI_WebApi.Model.DTOs.AITools;

public sealed class SearchDoctorsToolRequestDto
{
    public string SpaceId { get; set; } = string.Empty;
}

public sealed class SearchDoctorsToolResponseDto
{
    public string Result { get; set; } = string.Empty;
    public string DoctorNames { get; set; } = string.Empty;
    public string DoctorIds { get; set; } = "0";
    public string DoctorPhones { get; set; } = string.Empty;
    public string Command { get; set; } = string.Empty;
    public int Count { get; set; }
}

public sealed class SceneOperationToolRequestDto
{
    public string SpaceId { get; set; } = string.Empty;

    [Required]
    public string Target { get; set; } = string.Empty;
}

public sealed class SceneOperationToolResponseDto
{
    public string Result { get; set; } = string.Empty;
    public string IntentType { get; set; } = "unknown";
    public string MatchName { get; set; } = string.Empty;
    public decimal Score { get; set; }
}
