using AI_WebApi.Model.AITools;
using AI_WebApi.Model.Domain.IReGo;
using AI_WebApi.Model.DTOs.AITools;
using Microsoft.Extensions.Options;

namespace AI_WebApi.Security;

public interface IDifySystemContextResolver
{
    TrustedPatientContextResolution Resolve(
        HttpContext httpContext,
        DifySystemContextDto? systemContext);
}

public sealed class TrustedPatientContextResolution
{
    public TrustedPatientContext? Context { get; init; }
    public int HttpStatusCode { get; init; } = StatusCodes.Status200OK;
    public string? ReasonCode { get; init; }
    public bool IsSuccess => Context != null;
}

/// <summary>
/// Authenticates the Dify tool caller and resolves identity values that Dify injects
/// as system/runtime parameters. The LLM must not generate these values.
/// </summary>
public sealed class DifySystemContextResolver : IDifySystemContextResolver
{
    private readonly DifySystemContextValidator _validator;

    public DifySystemContextResolver(IOptions<IReGoToolOptions> options)
    {
        _validator = new DifySystemContextValidator(options.Value);
    }

    public TrustedPatientContextResolution Resolve(
        HttpContext httpContext,
        DifySystemContextDto? systemContext)
    {
        var authorization = Header(httpContext, "Authorization");
        var traceId =
            Header(httpContext, "X-Request-Id") ??
            httpContext.TraceIdentifier;
        var result = _validator.Validate(
            authorization,
            systemContext,
            traceId);
        if (result.IsSuccess)
        {
            return new TrustedPatientContextResolution
            {
                Context = result.Context
            };
        }

        var statusCode = result.ReasonCode switch
        {
            IReGoReasonCodes.DifyServiceAuthenticationNotConfigured =>
                StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status401Unauthorized
        };
        return Failure(statusCode, result.ReasonCode!);
    }

    private static string? Header(HttpContext context, string name)
    {
        var value = context.Request.Headers[name].FirstOrDefault();
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static TrustedPatientContextResolution Failure(
        int httpStatusCode,
        string reasonCode) => new()
        {
            HttpStatusCode = httpStatusCode,
            ReasonCode = reasonCode
        };
}
