namespace AI_WebApi.Model.AITools;

public sealed class AssessmentToolOptions
{
    public const string SectionName = "AssessmentTools";
    public const string ContractVersion = "assessment-2.0.0";
    public string ReferenceSigningKey { get; set; } = string.Empty;
    public string ReportTemplateVersion { get; set; } = "assessment-records-v1";
}
