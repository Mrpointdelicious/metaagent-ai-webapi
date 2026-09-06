using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using AI_WebApi.IServices.AITools;
using AI_WebApi.Model.AITools;
using AI_WebApi.Model.DTOs.AITools;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AI_WebApi.Services.AITools.HospitalOperations;

public sealed class HospitalOperationsArtifactIdentityProvider :
    IHospitalOperationsArtifactIdentityProvider
{
    private const string Prefix = "hospital_report_v2_";
    private const int SignatureBytes = 16;
    private readonly byte[] _key;
    private readonly HospitalOperationsOptions _options;

    public HospitalOperationsArtifactIdentityProvider(
        IOptions<HospitalOperationsOptions> options)
    {
        _options = options.Value;
        _key = Encoding.UTF8.GetBytes(
            _options.ArtifactRefSigningKey?.Trim() ?? string.Empty);
    }

    public bool IsConfigured => _key.Length >= 32;

    public HospitalOperationsArtifactIdentityDto Create(
        HospitalOperationsResultDto result)
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException(
                "Hospital report artifact signing key is not configured.");
        }

        var query = new JObject
        {
            ["artifact_contract"] = "hospital-report-v2",
            ["scope"] = result.Scope,
            ["hospital_refs"] = new JArray(result.Hospitals
                .Select(x => x.Hospital.HospitalId)
                .Distinct()
                .OrderBy(x => x)),
            ["section"] = result.Section,
            ["start_date"] = result.Period.StartDate,
            ["end_date"] = result.Period.EndDate,
            ["analysis_depth"] = result.AnalysisDepth,
            ["locale"] = "zh-CN",
            ["render_profile"] = NormalizeVersion(
                _options.RenderProfile,
                "html-png-v1")
        };
        var queryFingerprint = Fingerprint(CanonicalJson(query));

        var content = JObject.FromObject(
            result,
            JsonSerializer.Create(new JsonSerializerSettings
            {
                Culture = CultureInfo.InvariantCulture,
                NullValueHandling = NullValueHandling.Include,
                DateFormatHandling = DateFormatHandling.IsoDateFormat
            }));
        content.Property("report", StringComparison.OrdinalIgnoreCase)?.Remove();
        var contentFingerprint = Fingerprint(CanonicalJson(content));

        var contractVersion = HospitalOperationsContract.Version;
        var analysisRuleVersion = NormalizeVersion(
            _options.AnalysisRuleVersion,
            "hospital-analysis-rules-v2");
        var templateVersion = NormalizeVersion(
            _options.TemplateVersion,
            "hospital-operations-v3");
        var renderProfile = NormalizeVersion(
            _options.RenderProfile,
            "html-png-v1");
        var payload = string.Join(
            "|",
            "hospital-report-artifact",
            "v2",
            queryFingerprint,
            contentFingerprint,
            contractVersion,
            analysisRuleVersion,
            templateVersion,
            renderProfile,
            "html+png");

        using var hmac = new HMACSHA256(_key);
        var signature = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        var artifactRef = Prefix + Convert.ToHexString(
                signature.AsSpan(0, SignatureBytes))
            .ToLowerInvariant();
        return new HospitalOperationsArtifactIdentityDto
        {
            ArtifactRef = artifactRef,
            QueryFingerprint = queryFingerprint,
            ContentFingerprint = contentFingerprint,
            ContractVersion = contractVersion,
            AnalysisRuleVersion = analysisRuleVersion,
            TemplateVersion = templateVersion,
            RenderProfile = renderProfile
        };
    }

    private static string NormalizeVersion(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static string Fingerprint(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();

    private static string CanonicalJson(JToken token)
    {
        var builder = new StringBuilder();
        AppendCanonical(builder, token);
        return builder.ToString();
    }

    private static void AppendCanonical(StringBuilder builder, JToken token)
    {
        switch (token.Type)
        {
            case JTokenType.Object:
                builder.Append('{');
                var firstProperty = true;
                foreach (var property in ((JObject)token).Properties()
                             .OrderBy(x => x.Name, StringComparer.Ordinal))
                {
                    if (!firstProperty)
                    {
                        builder.Append(',');
                    }
                    firstProperty = false;
                    builder.Append(JsonConvert.ToString(property.Name));
                    builder.Append(':');
                    AppendCanonical(builder, property.Value);
                }
                builder.Append('}');
                return;
            case JTokenType.Array:
                builder.Append('[');
                var firstItem = true;
                foreach (var item in token.Children())
                {
                    if (!firstItem)
                    {
                        builder.Append(',');
                    }
                    firstItem = false;
                    AppendCanonical(builder, item);
                }
                builder.Append(']');
                return;
            case JTokenType.Integer:
                builder.Append(Convert.ToString(
                    ((JValue)token).Value,
                    CultureInfo.InvariantCulture));
                return;
            case JTokenType.Float:
                AppendNumber(builder, ((JValue)token).Value);
                return;
            case JTokenType.Boolean:
                builder.Append(token.Value<bool>() ? "true" : "false");
                return;
            case JTokenType.Null:
            case JTokenType.Undefined:
                builder.Append("null");
                return;
            case JTokenType.Date:
                var date = token.Value<DateTimeOffset>();
                builder.Append(JsonConvert.ToString(
                    date.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)));
                return;
            default:
                builder.Append(JsonConvert.ToString(
                    token.Value<string>() ?? token.ToString(Formatting.None)));
                return;
        }
    }

    private static void AppendNumber(StringBuilder builder, object? value)
    {
        switch (value)
        {
            case decimal decimalValue:
                builder.Append(decimalValue.ToString(
                    "0.#############################",
                    CultureInfo.InvariantCulture));
                break;
            case double doubleValue:
                builder.Append(doubleValue.ToString("R", CultureInfo.InvariantCulture));
                break;
            case float floatValue:
                builder.Append(floatValue.ToString("R", CultureInfo.InvariantCulture));
                break;
            default:
                builder.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
                break;
        }
    }
}
