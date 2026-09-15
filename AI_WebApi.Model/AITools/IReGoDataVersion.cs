namespace AI_WebApi.Model.AITools;

/// <summary>One data generation shared by references, caches and report artifacts.</summary>
public static class IReGoDataVersion
{
    public const string Namespace = "project";
    public const string Generation = "project-20260915";
    public const string ReferenceVersion = "v2";
    public const string SessionPrefix = "irego_s_v2_";
    public const string PlanPrefix = "irego_p_v2_";
    public const string PatientPrefix = "irego_patient_v2_";
    public const string TenantPrefix = "irego_tenant_v2_";
    public const string SnapshotPrefix = "ctx_v2_";
    public const string CacheNamespace = "irego:" + Namespace + ":" + Generation + ":v2";
    public const string ArtifactPrefix = "irego_img_v3_";
    public const string ArtifactSchema = "irego-artifact-v3";
    public const string ImageTokenVersion = "v2";
    public const string StorageDirectory = "project-20260915-v2";
    public const string RetiredSessionMessage =
        "系统已切换至新版数据。旧会话引用已停用，不再可用，请重新查询训练记录后选择记录。";
    public const string RetiredReportMessage =
        "旧报告链接已停用，不再可用，请重新生成报告。";

    public static bool IsRetiredSession(string? value) =>
        value?.StartsWith("irego_s_v1_", StringComparison.Ordinal) == true;

    public static bool IsRetiredArtifact(string? value) =>
        value?.StartsWith("irego_img_v1_", StringComparison.Ordinal) == true ||
        value?.StartsWith("irego_img_v2_", StringComparison.Ordinal) == true;

    public static bool IsRetiredImageToken(string? value) =>
        value?.StartsWith("v1.", StringComparison.Ordinal) == true;

    public static bool IsCurrentContext(string? value) =>
        value?.StartsWith(CacheNamespace + "|", StringComparison.Ordinal) == true;

    public static bool HasHexBody(string? value, string prefix, int length) =>
        value != null && value.StartsWith(prefix, StringComparison.Ordinal) &&
        value.Length == prefix.Length + length && value.Skip(prefix.Length).All(Uri.IsHexDigit);

    public static bool IsCurrentSession(string? value) => HasHexBody(value, SessionPrefix, 32);
}
