using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace AI_WebApi.Services.AITools.PatientContext;

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public sealed class PatientIdentityProof
{
    public string Purpose { get; init; } = "patient_rehab_v2";
    public string Subject { get; init; } = string.Empty;
    public int AnchorUserId { get; init; }
    public string ActorRole { get; init; } = string.Empty;
    public int? CurrentBusinessId { get; init; }
    public bool AllowAllTenants { get; init; }
    public string? PhoneBindingHash { get; init; }
    public long IssuedAt { get; init; }
    public long ExpiresAt { get; init; }
}

/// <summary>Issued by the authenticated business backend, never by a model-facing endpoint.</summary>
public static class PatientIdentityProofCodec
{
    private const string Prefix = "patient_identity_v2";
    public static string Issue(PatientIdentityProof proof, string key)
    {
        if (Encoding.UTF8.GetByteCount(key) < 32) throw new ArgumentException("Identity signing key requires 32 bytes.", nameof(key));
        var body = Base64Url(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(proof)));
        var signed = Prefix + "." + body;
        return signed + "." + Base64Url(HMACSHA256.HashData(Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes(signed)));
    }

    public static PatientIdentityProof? Verify(string? token, string key, DateTimeOffset now, int maximumLifetimeSeconds)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 8192 || Encoding.UTF8.GetByteCount(key) < 32) return null;
        try
        {
            var parts = token.Split('.');
            if (parts.Length != 3 || parts[0] != Prefix) return null;
            var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes(parts[0] + "." + parts[1]));
            if (!CryptographicOperations.FixedTimeEquals(expected, Decode(parts[2]))) return null;
            var value = JsonConvert.DeserializeObject<PatientIdentityProof>(Encoding.UTF8.GetString(Decode(parts[1])));
            var seconds = now.ToUnixTimeSeconds();
            return value != null && value.Purpose == "patient_rehab_v2" && value.ActorRole == "patient" && value.AnchorUserId > 0 &&
                !string.IsNullOrWhiteSpace(value.Subject) && value.Subject.Length <= 256 && value.IssuedAt <= seconds + 30 &&
                value.ExpiresAt > seconds && value.ExpiresAt > value.IssuedAt &&
                value.ExpiresAt - value.IssuedAt <= Math.Clamp(maximumLifetimeSeconds, 30, 900) ? value : null;
        }
        catch (Exception e) when (e is FormatException or JsonException or ArgumentException) { return null; }
    }

    public static string PhoneBindingHash(string? phone, string key) => Convert.ToHexString(HMACSHA256.HashData(
        Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes("phone-binding-v2|" + (phone?.Trim() ?? "")))).ToLowerInvariant();
    private static string Base64Url(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static byte[] Decode(string value) => Convert.FromBase64String(value.Replace('-', '+').Replace('_', '/') + new string('=', (4 - value.Length % 4) % 4));
}
