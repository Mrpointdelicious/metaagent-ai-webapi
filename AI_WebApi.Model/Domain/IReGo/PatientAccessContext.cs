using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using AI_WebApi.Model.AITools;
using AI_WebApi.Model.DTOs.AITools;

namespace AI_WebApi.Model.Domain.IReGo;

public sealed class PatientAccessContext
{
    public int AnchorUserId { get; init; }
    public string Subject { get; init; } = string.Empty;
    public int? CurrentBusinessId { get; init; }
    public string DataScope { get; init; } = "all_tenants";
    public int[] UserIds { get; init; } = Array.Empty<int>();
    public string ScopeFingerprint { get; init; } = string.Empty;
    public IReadOnlyDictionary<int, string?> Institutions { get; init; } = new Dictionary<int, string?>();
    public bool WalkUserIdsUseProjectNamespace { get; init; }
    public IReadOnlyDictionary<int, ConfirmedWalkRecordBinding> WalkRecords { get; init; } = new Dictionary<int, ConfirmedWalkRecordBinding>();
    private readonly ConcurrentDictionary<string, byte> _issues = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, RecordTenantDto> _records = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _owners = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, (RecordTenantDto Tenant, string? Owner)> _references = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _referenceRecords = new(StringComparer.Ordinal);
    private readonly byte[] _key;

    public PatientAccessContext(string referenceKey) => _key = Encoding.UTF8.GetBytes(referenceKey);
    public bool AllowsOwner(int? id) => id.HasValue && UserIds.Contains(id.Value);
    public bool AllowsTenant(int? id) => DataScope == "all_tenants" || id > 0 && id == CurrentBusinessId;
    public void AddIssue(string issue) => _issues.TryAdd(issue, 0);
    public string Reference(string kind, string value) => kind + "_" + Convert.ToHexString(
        HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes("patient-access-v2|" + kind + "|" + value))).ToLowerInvariant()[..32];

    public RecordTenantDto Tenant(int? id, string basis)
    {
        if (id is not > 0) return new();
        Institutions.TryGetValue(id.Value, out var name);
        return new RecordTenantDto
        {
            TenantRef = Reference("tenant", id.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            DisplayName = string.IsNullOrWhiteSpace(name) ? "所属机构名称暂缺" : name,
            AttributionStatus = "verified", AttributionBasis = basis,
            NameSource = string.IsNullOrWhiteSpace(name) ? "unavailable" : "current_directory"
        };
    }

    public RecordTenantDto Observe(string sourceRecord, int? tenant, string basis, int? ownerId = null)
    {
        var value = Tenant(tenant, basis);
        _records[sourceRecord] = value;
        if (ownerId.HasValue) _owners[sourceRecord] = Reference("owner", "project.dbuser|" + ownerId);
        if (value.AttributionStatus != "verified") AddIssue("RECORD_TENANT_UNVERIFIED");
        return value;
    }

    public void BindReference(string reference, string sourceRecord)
    {
        if (_records.TryGetValue(sourceRecord, out var tenant))
        { _owners.TryGetValue(sourceRecord, out var owner); _references[reference] = (tenant, owner); _referenceRecords[reference] = sourceRecord; }
    }
    public (RecordTenantDto? Tenant, string? Owner) Attribution(string reference) =>
        _references.TryGetValue(reference, out var result) ? result : (null, null);
    public string RecordKey(string reference) => _referenceRecords.TryGetValue(reference, out var record) ? record : reference;

    public PatientQueryScopeDto Describe() => new()
    {
        Requested = DataScope, Effective = DataScope,
        CurrentTenantRef = CurrentBusinessId > 0 ? Tenant(CurrentBusinessId, "authenticated_session").TenantRef : null,
        IncludedAccountCount = UserIds.Length,
        IncludedTenantCount = _records.Values.Where(x => x.TenantRef != null).Select(x => x.TenantRef).Distinct().Count(),
        UnattributedTenantRecordCount = _records.Values.Count(x => x.AttributionStatus != "verified"),
        Coverage = _issues.IsEmpty ? "confirmed_sources" : "partial",
        ScopeFingerprint = ScopeFingerprint, Issues = _issues.Keys.OrderBy(x => x, StringComparer.Ordinal).ToArray()
    };
}
