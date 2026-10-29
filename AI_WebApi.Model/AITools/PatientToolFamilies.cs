using AI_WebApi.Model.DTOs.AITools;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace AI_WebApi.Model.AITools;

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed record PatientToolFamily(string ObjectType, string IdentitySource, string ContractVersion,
    string ImplementationStatus, IReadOnlyDictionary<string, string> Tools);

public static class PatientToolFamilies
{
    public const string Version = "patient-tool-families-2.0.0";
    public static IReadOnlyList<PatientToolFamily> All { get; } = new[]
    {
        Family("irego", "project", IReGoContract.Version),
        Family("iretour", "project", "iretour-2.0.0"),
        Family("irecog", "project", "irecog-2.0.0"),
        new PatientToolFamily("assessment", "project", AssessmentToolOptions.ContractVersion, "implemented", new Dictionary<string, string>
        {
            ["overview"] = "get_assessment_patient_context", ["history"] = "get_assessment_patient_history",
            ["single"] = "get_assessment_result_analysis", ["multiple"] = "get_assessment_longitudinal_analysis",
            ["single_report"] = "generate_assessment_single_report", ["multiple_report"] = "generate_assessment_longitudinal_report"
        }),
        new PatientToolFamily("iremo", "project", "not_available", "blocked_missing_verified_training_results", new Dictionary<string, string>())
    };

    public static PatientToolFamily? Find(string? name) => All.FirstOrDefault(f => name != null && f.Tools.Values.Contains(name));
    private static PatientToolFamily Family(string name, string identity, string version) => new(name, identity, version, "implemented", new Dictionary<string, string>
    {
        ["overview"] = $"get_{name}_patient_context", ["history"] = $"get_{name}_patient_history",
        ["single"] = $"get_{name}_session_analysis", ["multiple"] = $"get_{name}_longitudinal_analysis",
        ["single_report"] = $"generate_{name}_single_session_report", ["multiple_report"] = $"generate_{name}_longitudinal_report"
    });
}
