using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AI_WebApi.IServices.AITools;
using AI_WebApi.Model.AITools;
using Microsoft.Extensions.Options;

namespace AI_WebApi.Services.AITools.IReGo;

/// <summary>Deterministic current-generation references, independent of cache lifetime.</summary>
public sealed class IReGoSessionRefProvider : IIReGoSessionRefProvider
{
    private readonly byte[] _key;
    public IReGoSessionRefProvider(IOptions<IReGoToolOptions> options) =>
        _key = Encoding.UTF8.GetBytes(options.Value.SessionRefSigningKey?.Trim() ?? string.Empty);

    public bool IsConfigured => _key.Length >= 32;

    public string Create(int? tenantId, int patientId, int planId, int reportId, string schemaSignature) =>
        CreateReference(IReGoDataVersion.SessionPrefix, "session", tenantId, patientId, planId, reportId, schemaSignature);

    public bool Matches(string sessionRef, int? tenantId, int patientId, int planId, int reportId, string schemaSignature)
    {
        if (!IsConfigured || !IReGoDataVersion.IsCurrentSession(sessionRef)) return false;
        var offset = IReGoDataVersion.SessionPrefix.Length;
        var supplied = Convert.FromHexString(sessionRef[offset..]);
        var expected = Convert.FromHexString(Create(tenantId, patientId, planId, reportId, schemaSignature)[offset..]);
        return CryptographicOperations.FixedTimeEquals(supplied, expected);
    }

    public string CreatePlan(int? tenantId, int patientId, int planId) =>
        CreateReference(IReGoDataVersion.PlanPrefix, "plan", tenantId, patientId, planId);

    public string CreatePatient(int? tenantId, int patientId) =>
        CreateReference(IReGoDataVersion.PatientPrefix, "patient", tenantId, patientId);

    public string CreateTenant(int tenantId) =>
        CreateReference(IReGoDataVersion.TenantPrefix, "tenant", tenantId);

    private string CreateReference(string prefix, string purpose, int? tenantId,
        int? patientId = null, int? planId = null, int? reportId = null, string? schemaSignature = null)
    {
        if (!IsConfigured) throw new InvalidOperationException("IREGO reference signing key is not configured.");
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("version", IReGoDataVersion.ReferenceVersion);
            writer.WriteString("namespace", IReGoDataVersion.Namespace);
            writer.WriteString("generation", IReGoDataVersion.Generation);
            writer.WriteString("purpose", purpose);
            if (tenantId.HasValue) writer.WriteNumber("tenant_id", tenantId.Value);
            else writer.WriteNull("tenant_id");
            if (patientId.HasValue) writer.WriteNumber("patient_id", patientId.Value);
            if (planId.HasValue) writer.WriteNumber("plan_id", planId.Value);
            if (reportId.HasValue) writer.WriteNumber("report_id", reportId.Value);
            if (purpose == "session") writer.WriteString("schema_signature", schemaSignature ?? string.Empty);
            writer.WriteEndObject();
        }
        using var hmac = new HMACSHA256(_key);
        return prefix + Convert.ToHexString(hmac.ComputeHash(stream.ToArray()).AsSpan(0, 16)).ToLowerInvariant();
    }
}
