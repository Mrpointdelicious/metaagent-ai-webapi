using System.Security.Cryptography;
using System.Text;
using AI_WebApi.IServices.AITools;
using AI_WebApi.Model.AITools;
using AI_WebApi.Model.Domain.IReTour;
using Microsoft.Extensions.Options;

namespace AI_WebApi.Services.AITools.IReTour;

/// <summary>
/// Issues and verifies stable anonymous IReTour session references with an
/// independent prefix and purpose domain, so IReGo and IReTour references can
/// never be parsed across domains. A reference exposes no database ids.
/// </summary>
public sealed class IReTourSessionRefProvider : IIReTourSessionRefProvider
{
    public const string Prefix = "iretour_s_v1_";
    private readonly IReTourToolOptions _options;
    private readonly AI_WebApi.IServices.AITools.IPatientAccessContextAccessor? _access;

    public IReTourSessionRefProvider(IOptions<IReTourToolOptions> options, AI_WebApi.IServices.AITools.IPatientAccessContextAccessor? access = null)
    {
        _access = access;
        _options = options.Value;
    }

    public string Issue(IReTourEvaluatedSession session)
    {
        var payload = string.Join(
            "|",
            "1",
            IReTourContract.DeviceFamily,
            session.BusinessId ?? 0,
            session.PatientId,
            session.CanonicalSessionKey,
            IReTourContract.SourceSystem,
            session.PatientBindingVersion);
        if (_access?.Current != null) payload += "|" + _access.Current.ScopeFingerprint;
        var reference = Prefix + Base64Url(Convert.ToBase64String(
            Hmac(Encoding.UTF8.GetBytes(payload)))) + "." +
            Base64Url(Convert.ToBase64String(Encoding.UTF8.GetBytes(payload)));
        _access?.Current?.BindReference(reference, $"project.dbwalk:{session.WalkId}");
        return reference;
    }

    public IReTourEvaluatedSession? Verify(
        string sessionRef,
        int patientId,
        IReadOnlyList<IReTourEvaluatedSession> candidates)
    {
        var key = _options.SessionRefSigningKey;
        if (string.IsNullOrWhiteSpace(key) ||
            !sessionRef.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return null;
        }

        var parts = sessionRef[Prefix.Length..].Split('.');
        if (parts.Length != 2)
        {
            return null;
        }

        byte[] payloadBytes;
        try
        {
            payloadBytes = Convert.FromBase64String(FromBase64Url(parts[1]));
            var expected = Convert.ToBase64String(Hmac(payloadBytes));
            if (!CryptographicOperations.FixedTimeEquals(
                    Convert.FromBase64String(FromBase64Url(parts[0])),
                    Convert.FromBase64String(expected)))
            {
                return null;
            }
        }
        catch (FormatException)
        {
            return null;
        }

        var payload = Encoding.UTF8.GetString(payloadBytes).Split('|');
        if (payload.Length != (_access?.Current == null ? 7 : 8) ||
            (_access?.Current != null && payload[7] != _access.Current.ScopeFingerprint) ||
            payload[0] != "1" ||
            payload[1] != IReTourContract.DeviceFamily ||
            payload[5] != IReTourContract.SourceSystem ||
            !int.TryParse(payload[3], out var signedPatient) ||
            signedPatient != patientId)
        {
            return null;
        }

        return candidates.FirstOrDefault(x =>
            x.CanonicalSessionKey == payload[4] && x.PatientId == patientId &&
            (x.BusinessId ?? 0).ToString(System.Globalization.CultureInfo.InvariantCulture) == payload[2] &&
            x.PatientBindingVersion.ToString(System.Globalization.CultureInfo.InvariantCulture) == payload[6] &&
            x.Visible);
    }

    private byte[] Hmac(byte[] payload) => HMACSHA256.HashData(
        Encoding.UTF8.GetBytes(_options.SessionRefSigningKey),
        payload);

    private static string Base64Url(string base64) =>
        base64.TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string FromBase64Url(string value) =>
        value.Replace('-', '+').Replace('_', '/')
            .PadRight(value.Length + (4 - value.Length % 4) % 4, '=');
}
