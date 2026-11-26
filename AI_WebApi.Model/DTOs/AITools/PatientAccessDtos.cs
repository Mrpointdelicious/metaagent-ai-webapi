using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace AI_WebApi.Model.DTOs.AITools;

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class PatientIdentityInputDto
{
    public string Type { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class RecordTenantDto
{
    public string? TenantRef { get; init; }
    public string DisplayName { get; init; } = "所属机构未确认";
    public string AttributionStatus { get; init; } = "unknown";
    public string AttributionBasis { get; init; } = "unverified";
    public string NameSource { get; init; } = "unavailable";
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class PatientQueryScopeDto
{
    public string ContractVersion { get; init; } = "patient-access-2.0.0";
    public string Requested { get; init; } = "all_tenants";
    public string Effective { get; init; } = "all_tenants";
    public string? CurrentTenantRef { get; init; }
    public int IncludedAccountCount { get; init; }
    public int IncludedTenantCount { get; init; }
    public int UnattributedTenantRecordCount { get; init; }
    public string Coverage { get; init; } = "confirmed_sources";
    public string ScopeFingerprint { get; init; } = string.Empty;
    public IReadOnlyList<string> Issues { get; init; } = Array.Empty<string>();
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class PatientAccessResolutionDto
{
    public string PatientRef { get; init; } = string.Empty;
    public int IncludedAccountCount { get; init; }
    public string BindingStatus { get; init; } = "resolved";
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class PatientTenantSegmentDto
{
    public RecordTenantDto RecordTenant { get; init; } = new();
    public IReadOnlyList<string> RecordRefs { get; init; } = Array.Empty<string>();
    public DateTimeOffset? FirstRecordedAt { get; init; }
    public DateTimeOffset? LastRecordedAt { get; init; }
    public int RecordCount => RecordRefs.Count;
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class ResolvePatientAccessRequestDto
{
    public DifySystemContextDto? SystemContext { get; init; }
    public string? DataScope { get; init; }
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class ReadPatientReportRequest
{
    public string OriginalGrant { get; init; } = string.Empty;
    public DifySystemContextDto? SystemContext { get; init; }
    public string DataScope { get; init; } = "all_tenants";
}
