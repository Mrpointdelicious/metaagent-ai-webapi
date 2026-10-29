using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AI_WebApi.Services.AITools.IReGo;

/// <summary>
/// Creates the opaque, deterministic identity of a persisted IREGO report.
/// Version 2 uses purpose-separated HMAC-SHA256 and a canonical JSON payload.
/// Version 1 references remain valid for historic chat messages.
/// </summary>
internal static class IReGoArtifactReference
{
    private const string Version1Prefix = "irego_img_v1_";
    private const string Version2Prefix = "irego_img_v2_";
    private const string ArtifactKeyPurpose =
        "ai-webapi/irego/artifact-ref/v2";

    public static string Create(
        byte[] masterKey,
        string patientRef,
        string trainingScopeRef,
        string sourceSnapshot,
        string templateVersion)
    {
        var payload = CanonicalPayload(
            patientRef,
            trainingScopeRef,
            sourceSnapshot,
            templateVersion);
        var artifactKey = Hmac(masterKey, ArtifactKeyPurpose);
        var signature = Hmac(artifactKey, payload);
        return (patientRef.StartsWith("patient_scope_v2_", StringComparison.Ordinal) ? "irego_scope_v2_" : Version2Prefix) +
               Convert.ToHexString(signature).ToLowerInvariant();
    }

    public static bool IsValid(string artifactRef)
    {
        if (string.IsNullOrWhiteSpace(artifactRef))
        {
            return false;
        }

        return HasHexBody(artifactRef, Version1Prefix, 32) ||
               HasHexBody(artifactRef, Version2Prefix, 64) || HasHexBody(artifactRef, "irego_scope_v2_", 64);
    }

    private static byte[] CanonicalPayload(
        string patientRef,
        string trainingScopeRef,
        string sourceSnapshot,
        string templateVersion)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("artifact_schema", "irego-artifact-v2");
            writer.WriteString("source_namespace", "project.dbrehaplan:20261023");
            writer.WriteString("patient_ref", patientRef.Trim());
            writer.WriteString("training_scope_ref", trainingScopeRef.Trim());
            writer.WriteString("source_snapshot", sourceSnapshot.Trim());
            writer.WriteString("template_version", templateVersion.Trim());
            writer.WriteString("format", "png");
            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    private static byte[] Hmac(byte[] key, string value) =>
        Hmac(key, Encoding.UTF8.GetBytes(value));

    private static byte[] Hmac(byte[] key, byte[] value)
    {
        using var hmac = new HMACSHA256(key);
        return hmac.ComputeHash(value);
    }

    private static bool HasHexBody(
        string artifactRef,
        string prefix,
        int hexLength) =>
        artifactRef.StartsWith(prefix, StringComparison.Ordinal) &&
        artifactRef.Length == prefix.Length + hexLength &&
        artifactRef.AsSpan(prefix.Length).ToString().All(Uri.IsHexDigit);
}
