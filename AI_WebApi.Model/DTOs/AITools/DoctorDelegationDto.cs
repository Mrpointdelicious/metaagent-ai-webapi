namespace AI_WebApi.Model.DTOs.AITools;

/// <summary>Server-issued, current-institution authorization; never model input.</summary>
public sealed class DoctorDelegationDto
{
    public int PatientId { get; init; }
    public int InstitutionId { get; init; }
    public int DoctorId { get; init; }
    public string AccountId { get; init; } = string.Empty;
    public string RelationshipRef { get; init; } = string.Empty;
    public int RelationshipVersion { get; init; }
    public string Workspace { get; init; } = string.Empty;
}
