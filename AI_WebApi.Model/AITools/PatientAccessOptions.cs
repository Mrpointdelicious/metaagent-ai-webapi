namespace AI_WebApi.Model.AITools;

public sealed class PatientAccessOptions
{
    public const string SectionName = "PatientAccess";
    public string IdentitySigningKey { get; set; } = string.Empty;
    public int MaximumProofLifetimeSeconds { get; set; } = 900;
    public bool RequireIdentityProof { get; set; } = true;
    public string DoctorDelegationSigningKey { get; set; } = string.Empty;
    public string DoctorDelegationValidationUrl { get; set; } = string.Empty;
    public string DoctorDelegationServiceToken { get; set; } = string.Empty;
}
