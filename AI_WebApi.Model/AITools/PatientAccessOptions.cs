namespace AI_WebApi.Model.AITools;

public sealed class PatientAccessOptions
{
    public const string SectionName = "PatientAccess";
    public string IdentitySigningKey { get; set; } = string.Empty;
    public int MaximumProofLifetimeSeconds { get; set; } = 900;
    public string DoctorDelegationSigningKey { get; set; } = string.Empty;
    public string DoctorDelegationValidationUrl { get; set; } = string.Empty;
    public string DoctorDelegationServiceToken { get; set; } = string.Empty;
    public List<ConfirmedWalkRecordBinding> ConfirmedWalkRecords { get; set; } = new();
}

public sealed class ConfirmedWalkRecordBinding
{
    public int WalkId { get; set; }
    public int ProjectUserId { get; set; }
    public int? BusinessId { get; set; }
}
