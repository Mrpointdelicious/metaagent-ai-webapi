using System.ComponentModel.DataAnnotations;

namespace AI_WebApi.Model.DTOs.AITools;

public class GetPatientRehabOverviewToolRequestDto
{
    [Required]
    [Range(1, int.MaxValue)]
    public int PatientId { get; set; }
}