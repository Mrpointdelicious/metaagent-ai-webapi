using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using AI_WebApi.Model.Domain.IReGo;
using AI_WebApi.Model.DTOs.AITools;

namespace AI_WebApi.Model.AITools;

public sealed class DifySystemContextValidationResult
{
    public TrustedPatientContext? Context { get; init; }
    public string? ReasonCode { get; init; }
    public bool IsSuccess => Context != null;
}

/// <summary>
/// Pure validation core for Dify service authentication and system-bound
/// identity parameters. It has no HTTP dependency so the trust rules remain
/// directly testable.
/// </summary>
public sealed class DifySystemContextValidator
{
    private readonly IReGoToolOptions _options;

    public DifySystemContextValidator(IReGoToolOptions options)
    {
        _options = options;
    }

    public DifySystemContextValidationResult Validate(
        string? authorization,
        DifySystemContextDto? systemContext,
        string traceId)
    {
        if (string.IsNullOrWhiteSpace(_options.DifyServiceBearerToken))
        {
            return Failure(
                IReGoReasonCodes.DifyServiceAuthenticationNotConfigured);
        }

        if (string.IsNullOrWhiteSpace(authorization))
        {
            return Failure(
                IReGoReasonCodes.DifyServiceAuthenticationRequired);
        }

        if (!TryGetBearerToken(authorization, out var bearerToken) ||
            !FixedTimeEquals(
                bearerToken,
                _options.DifyServiceBearerToken))
        {
            return Failure(
                IReGoReasonCodes.DifyServiceAuthenticationFailed);
        }

        if (systemContext == null)
        {
            return Failure(IReGoReasonCodes.DifySystemContextMissing);
        }

        var user = systemContext.User?.Trim();
        var conversationId = systemContext.ConversationId?.Trim();

        if (conversationId?.Length > 256)
        {
            return Failure(IReGoReasonCodes.InvalidDifySystemContext);
        }

        if (string.IsNullOrWhiteSpace(user) ||
            user.Length > 10 ||
            !int.TryParse(
                user,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var patientId) ||
            patientId <= 0)
        {
            return Failure(IReGoReasonCodes.DifyUserIdInvalid);
        }

        return new DifySystemContextValidationResult
        {
            Context = new TrustedPatientContext
            {
                PatientId = patientId,
                SubjectRef = $"dify_user:{patientId}",
                ConversationId = string.IsNullOrWhiteSpace(conversationId)
                    ? traceId
                    : conversationId,
                TraceId = traceId
            }
        };
    }

    private static bool TryGetBearerToken(
        string authorization,
        out string token)
    {
        const string prefix = "Bearer ";
        if (!authorization.StartsWith(
                prefix,
                StringComparison.OrdinalIgnoreCase))
        {
            token = string.Empty;
            return false;
        }

        token = authorization[prefix.Length..].Trim();
        return token.Length > 0;
    }

    private static bool FixedTimeEquals(string provided, string configured)
    {
        var providedBytes = Encoding.UTF8.GetBytes(provided);
        var configuredBytes = Encoding.UTF8.GetBytes(configured);
        return providedBytes.Length == configuredBytes.Length &&
            CryptographicOperations.FixedTimeEquals(
                providedBytes,
                configuredBytes);
    }

    private static DifySystemContextValidationResult Failure(
        string reasonCode) => new()
        {
            ReasonCode = reasonCode
        };
}
