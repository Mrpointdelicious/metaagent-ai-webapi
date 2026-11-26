using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

namespace AI_WebApi.Services.AITools.PatientContext;

public sealed record PatientArtifactGrant(string ArtifactRef, string ScopeFingerprint, string DataScope,
    PatientIdentityProof Identity, long ExpiresAt);

public static class PatientArtifactGrantCodec
{
    private const string Prefix = "patient_artifact_v2";
    public static string Issue(PatientArtifactGrant grant, string key)
    {
        var body = Encode(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(grant)));
        var message = Prefix + "." + body;
        return message + "." + Encode(HMACSHA256.HashData(Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes(message)));
    }
    public static PatientArtifactGrant? Verify(string? token, string key, DateTimeOffset now)
    {
        var grant = ReadSignedBinding(token, key);
        return grant != null && grant.ExpiresAt > now.ToUnixTimeSeconds() &&
            grant.ExpiresAt <= now.AddMinutes(5).ToUnixTimeSeconds() ? grant : null;
    }

    // Authenticated provenance of a saved report. Only Verify authorizes a download.
    public static PatientArtifactGrant? ReadSignedBinding(string? token, string key)
    {
        if (token == null || token.Length > 16384 || Encoding.UTF8.GetByteCount(key) < 32) return null;
        try
        {
            var parts = token.Split('.');
            if (parts.Length != 3 || parts[0] != Prefix) return null;
            var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes(parts[0] + "." + parts[1]));
            if (!CryptographicOperations.FixedTimeEquals(expected, Decode(parts[2]))) return null;
            var grant = JsonConvert.DeserializeObject<PatientArtifactGrant>(Encoding.UTF8.GetString(Decode(parts[1])));
            return grant?.Identity != null && grant.ExpiresAt <= grant.Identity.ExpiresAt &&
                grant.ArtifactRef?.StartsWith("irego_scope_v2_", StringComparison.Ordinal) == true ? grant : null;
        }
        catch (Exception e) when (e is FormatException or JsonException or ArgumentException or NullReferenceException) { return null; }
    }
    private static string Encode(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static byte[] Decode(string text) => Convert.FromBase64String(text.Replace('-', '+').Replace('_', '/') + new string('=', (4 - text.Length % 4) % 4));
}
