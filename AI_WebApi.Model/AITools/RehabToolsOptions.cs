namespace AI_WebApi.Model.AITools;

public sealed class RehabToolsOptions
{
    public const string SectionName = "RehabTools";

    public string DefaultSpaceId { get; set; } = string.Empty;
    public DoctorLookupOptions Doctor { get; set; } = new();
    public SceneNavigationOptions Scene { get; set; } = new();
}

public sealed class DoctorLookupOptions
{
    public string LoginUrl { get; set; } = string.Empty;
    public string QueryUrl { get; set; } = string.Empty;
    public string LoginPhone { get; set; } = string.Empty;
    public string LoginPassword { get; set; } = string.Empty;
    public int TokenTtlMinutes { get; set; } = 60;
    public int RequestTimeoutSeconds { get; set; } = 15;
}

public sealed class SceneNavigationOptions
{
    public string VersionUrl { get; set; } = string.Empty;
    public string QueryUrl { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string ApiToken { get; set; } = string.Empty;
    public int RequestTimeoutSeconds { get; set; } = 15;
    public SceneCommandResolverOptions Resolver { get; set; } = new();
}

public sealed class SceneCommandResolverOptions
{
    public decimal MinimumScore { get; set; } = .75m;
    public decimal MinimumMargin { get; set; } = .05m;
}
