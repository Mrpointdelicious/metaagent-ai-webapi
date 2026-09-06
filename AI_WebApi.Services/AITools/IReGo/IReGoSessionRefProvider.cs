using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using AI_WebApi.IServices.AITools;
using AI_WebApi.Model.AITools;
using Microsoft.Extensions.Options;

namespace AI_WebApi.Services.AITools.IReGo;

/// <summary>
/// Creates opaque, deterministic session references that remain resolvable
/// without process-local state.
/// </summary>
public sealed class IReGoSessionRefProvider : IIReGoSessionRefProvider
{
    private const string Version = "v1";
    private const string Prefix = "irego_s_v1_";
    private const string PlanPrefix = "irego_p_v1_";
    private const string PatientPrefix = "irego_patient_v1_";
    private const string TenantPrefix = "irego_tenant_v1_";
    private const string SourceSystem = "robotdb_main";
    private const int SignatureByteCount = 16;

    private readonly byte[] _key;

    public IReGoSessionRefProvider(IOptions<IReGoToolOptions> options)
    {
        _key = Encoding.UTF8.GetBytes(
            options.Value.SessionRefSigningKey?.Trim() ?? string.Empty);
    }

    public bool IsConfigured => _key.Length >= 32;

    public string Create(
        int? tenantId,
        int patientId,
        int planId,
        int reportId,
        string schemaSignature)
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException(
                "IREGO session reference signing key is not configured.");
        }

        var payload = BuildPayload(
            tenantId,
            patientId,
            planId,
            reportId,
            schemaSignature);
        using var hmac = new HMACSHA256(_key);
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        var signature = Convert.ToHexString(
            hash.AsSpan(0, SignatureByteCount)).ToLowerInvariant();
        return $"{Prefix}{signature}";
    }

    public bool Matches(
        string sessionRef,
        int? tenantId,
        int patientId,
        int planId,
        int reportId,
        string schemaSignature)
    {
        if (!IsConfigured ||
            string.IsNullOrWhiteSpace(sessionRef) ||
            !sessionRef.StartsWith(Prefix, StringComparison.Ordinal) ||
            sessionRef.Length != Prefix.Length + SignatureByteCount * 2)
        {
            return false;
        }

        byte[] supplied;
        try
        {
            supplied = Convert.FromHexString(sessionRef[Prefix.Length..]);
        }
        catch (FormatException)
        {
            return false;
        }

        var expected = Convert.FromHexString(
            Create(
                tenantId,
                patientId,
                planId,
                reportId,
                schemaSignature)[Prefix.Length..]);
        return CryptographicOperations.FixedTimeEquals(supplied, expected);
    }

    public string CreatePlan(
        int? tenantId,
        int patientId,
        int planId)
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException(
                "IREGO plan reference signing key is not configured.");
        }

        var payload = string.Join(
            "|",
            Version,
            SourceSystem,
            "plan",
            tenantId?.ToString(CultureInfo.InvariantCulture) ?? "none",
            patientId.ToString(CultureInfo.InvariantCulture),
            planId.ToString(CultureInfo.InvariantCulture));
        using var hmac = new HMACSHA256(_key);
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        var signature = Convert.ToHexString(
            hash.AsSpan(0, SignatureByteCount)).ToLowerInvariant();
        return $"{PlanPrefix}{signature}";
    }

    public string CreatePatient(int? tenantId, int patientId) =>
        CreateSubjectReference(
            PatientPrefix,
            "patient",
            tenantId?.ToString(CultureInfo.InvariantCulture) ?? "none",
            patientId.ToString(CultureInfo.InvariantCulture));

    public string CreateTenant(int tenantId) =>
        CreateSubjectReference(
            TenantPrefix,
            "tenant",
            tenantId.ToString(CultureInfo.InvariantCulture));

    private string CreateSubjectReference(
        string prefix,
        params string[] components)
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException(
                "IREGO reference signing key is not configured.");
        }

        var payload = string.Join(
            "|",
            new[] { Version, SourceSystem }.Concat(components));
        using var hmac = new HMACSHA256(_key);
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        var signature = Convert.ToHexString(
            hash.AsSpan(0, SignatureByteCount)).ToLowerInvariant();
        return $"{prefix}{signature}";
    }

    private static string BuildPayload(
        int? tenantId,
        int patientId,
        int planId,
        int reportId,
        string schemaSignature) =>
        string.Join(
            "|",
            Version,
            SourceSystem,
            tenantId?.ToString(CultureInfo.InvariantCulture) ?? "none",
            patientId.ToString(CultureInfo.InvariantCulture),
            planId.ToString(CultureInfo.InvariantCulture),
            reportId.ToString(CultureInfo.InvariantCulture),
            schemaSignature ?? string.Empty);
}
