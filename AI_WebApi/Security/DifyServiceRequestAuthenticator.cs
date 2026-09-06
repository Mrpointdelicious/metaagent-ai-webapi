using System.Security.Cryptography;
using System.Text;
using AI_WebApi.Model.AITools;
using Microsoft.Extensions.Options;

namespace AI_WebApi.Security;

public interface IDifyServiceRequestAuthenticator
{
    DifyServiceAuthenticationResult Authenticate(HttpContext httpContext);
}

public sealed class DifyServiceAuthenticationResult
{
    public bool IsSuccess { get; init; }
    public int HttpStatusCode { get; init; } = StatusCodes.Status200OK;
    public string? ReasonCode { get; init; }
}

public sealed class DifyServiceRequestAuthenticator :
    IDifyServiceRequestAuthenticator
{
    private readonly IReGoToolOptions _options;

    public DifyServiceRequestAuthenticator(IOptions<IReGoToolOptions> options)
    {
        _options = options.Value;
    }

    public DifyServiceAuthenticationResult Authenticate(HttpContext httpContext)
    {
        var configured = _options.DifyServiceBearerToken?.Trim();
        if (string.IsNullOrWhiteSpace(configured))
        {
            return Failure(
                StatusCodes.Status503ServiceUnavailable,
                HospitalOperationsReasonCodes.AuthenticationNotConfigured);
        }

        var authorization = httpContext.Request.Headers.Authorization
            .FirstOrDefault()?.Trim();
        const string prefix = "Bearer ";
        if (string.IsNullOrWhiteSpace(authorization) ||
            !authorization.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return Failure(
                StatusCodes.Status401Unauthorized,
                HospitalOperationsReasonCodes.AuthenticationRequired);
        }

        var supplied = authorization[prefix.Length..].Trim();
        var suppliedBytes = Encoding.UTF8.GetBytes(supplied);
        var configuredBytes = Encoding.UTF8.GetBytes(configured);
        if (suppliedBytes.Length != configuredBytes.Length ||
            !CryptographicOperations.FixedTimeEquals(
                suppliedBytes,
                configuredBytes))
        {
            return Failure(
                StatusCodes.Status401Unauthorized,
                HospitalOperationsReasonCodes.AuthenticationFailed);
        }

        return new DifyServiceAuthenticationResult { IsSuccess = true };
    }

    private static DifyServiceAuthenticationResult Failure(
        int status,
        string reason) => new()
        {
            HttpStatusCode = status,
            ReasonCode = reason
        };
}
