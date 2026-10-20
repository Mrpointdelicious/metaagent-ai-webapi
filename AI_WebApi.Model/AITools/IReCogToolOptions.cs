namespace AI_WebApi.Model.AITools;

public sealed class IReCogToolOptions
{
    public const string SectionName = "IReCogTools";
    public const string ContractVersion = "irecog-1.0.0";
    public string SessionRefSigningKey { get; set; } = string.Empty;
    public string ReportTemplateVersion { get; set; } = "irecog-observed-assessment-v1";
}
