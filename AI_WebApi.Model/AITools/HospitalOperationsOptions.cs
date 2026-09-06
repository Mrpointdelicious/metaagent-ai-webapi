namespace AI_WebApi.Model.AITools;

public sealed class HospitalOperationsOptions
{
    public const string SectionName = "HospitalOperations";

    public string UpstreamBaseUrl { get; set; } = string.Empty;
    public string PublicBaseUrl { get; set; } = string.Empty;
    public int RequestTimeoutSeconds { get; set; } = 15;
    public int DirectoryCacheSeconds { get; set; } = 300;
    public string ArtifactRefSigningKey { get; set; } = string.Empty;
    public string ArtifactStoragePath { get; set; } =
        "data/hospital-operation-reports";
    public int MaxArtifactBytes { get; set; } = 8 * 1024 * 1024;
    public string AnalysisRuleVersion { get; set; } =
        "hospital-analysis-rules-v2";
    public string TemplateVersion { get; set; } = "hospital-operations-v3";
    public string RenderProfile { get; set; } = "html-png-v1";
}

public static class HospitalOperationsContract
{
    public const string Version = "1.2.0";
}

public static class HospitalOperationsReasonCodes
{
    public const string InvalidRequest = "INVALID_REQUEST";
    public const string AuthenticationRequired =
        "DIFY_SERVICE_AUTHENTICATION_REQUIRED";
    public const string AuthenticationFailed =
        "DIFY_SERVICE_AUTHENTICATION_FAILED";
    public const string AuthenticationNotConfigured =
        "DIFY_SERVICE_AUTHENTICATION_NOT_CONFIGURED";
    public const string HospitalNotFound = "HOSPITAL_NOT_FOUND";
    public const string HospitalIdentityMismatch =
        "HOSPITAL_IDENTITY_MISMATCH";
    public const string ComparisonRequiresExactlyTwoHospitals =
        "COMPARISON_REQUIRES_EXACTLY_TWO_HOSPITALS";
    public const string UpstreamUnavailable = "UPSTREAM_UNAVAILABLE";
    public const string ReportRenderingUnavailable =
        "REPORT_RENDERING_UNAVAILABLE";
    public const string ReportArtifactNotConfigured =
        "REPORT_ARTIFACT_NOT_CONFIGURED";
}
