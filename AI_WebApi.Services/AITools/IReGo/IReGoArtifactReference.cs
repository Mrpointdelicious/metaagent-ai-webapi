using AI_WebApi.Model.AITools;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AI_WebApi.Services.AITools.IReGo;

/// <summary>
/// Creates the opaque, deterministic identity of a persisted IREGO report.
/// Version 3 uses purpose-separated HMAC-SHA256 and the project data generation.
/// All pre-migration references are retired.
/// </summary>
internal static class IReGoArtifactReference
{
    private const string ArtifactKeyPurpose =
        "ai-webapi/irego/artifact-ref/v3";

    public static string Create(
        byte[] masterKey,
        string patientRef,
        string trainingScopeRef,
        string sourceSnapshot,
        string templateVersion)
    {
        if (patientRef.StartsWith("irego_patient_v1_", StringComparison.Ordinal) ||
            IReGoDataVersion.IsRetiredSession(trainingScopeRef) ||
            trainingScopeRef.StartsWith("irego_single_scope_v1_", StringComparison.Ordinal) ||
            trainingScopeRef.StartsWith("irego_window_scope_v1_", StringComparison.Ordinal))
            throw new InvalidOperationException(IReGoDataVersion.RetiredReportMessage);
        var payload = CanonicalPayload(
            patientRef,
            trainingScopeRef,
            sourceSnapshot,
            templateVersion);
        var artifactKey = Hmac(masterKey, ArtifactKeyPurpose);
        var signature = Hmac(artifactKey, payload);
        return IReGoDataVersion.ArtifactPrefix +
               Convert.ToHexString(signature).ToLowerInvariant();
    }

    public static bool IsValid(string artifactRef)
    {
        if (string.IsNullOrWhiteSpace(artifactRef))
        {
            return false;
        }

        return IReGoDataVersion.HasHexBody(artifactRef, IReGoDataVersion.ArtifactPrefix, 64);
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
            writer.WriteString("artifact_schema", IReGoDataVersion.ArtifactSchema);
            writer.WriteString("source_namespace", IReGoDataVersion.Namespace);
            writer.WriteString("data_generation", IReGoDataVersion.Generation);
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

}
