using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace AI_WebApi.Model.DTOs.AITools;

/// <summary>
/// Dify supplies either the phone captured at the conversation boundary
/// or a current project.dbuser ID. Neither value may be
/// inferred by the language model.
/// </summary>
[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class ResolvePatientIdentityRequestDto
{
    public int ProjectPatientId { get; init; }
    public string? Phone { get; init; }
}

/// <summary>
/// Both identity fields contain the same current project.dbuser ID.
/// MatchMethod retains its historical protocol value; no name matching occurs.
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
