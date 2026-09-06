using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace AI_WebApi.Model.DTOs.AITools;

/// <summary>
/// Temporary bridge input. Dify supplies either the phone captured at the
/// conversation boundary or a legacy project patient ID. Neither value may be
/// inferred by the language model.
/// </summary>
[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class ResolvePatientIdentityRequestDto
{
    public int ProjectPatientId { get; init; }
    public string? Phone { get; init; }
}

/// <summary>
/// The robotdb patient identity resolved by exact name plus the backend-owned
/// patient user type. Names are deliberately not returned to the caller.
/// </summary>
[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class PatientIdentityResolutionDto
{
    public int ProjectPatientId { get; init; }
    public int RobotPatientId { get; init; }
    public string MatchMethod { get; init; } =
        "exact_name_and_patient_type";
    public string BindingStatus { get; init; } = "resolved";
}
