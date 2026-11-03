using System.Net.Http.Json;
using AI_WebApi.IServices.AITools;
using AI_WebApi.Model.AITools;
using AI_WebApi.Model.DTOs.AITools;
using Microsoft.Extensions.Options;

namespace AI_WebApi.Services.AITools.PatientContext;

public sealed class DoctorDelegationValidator : IDoctorDelegationValidator
{
    private readonly IHttpClientFactory _clients;
    private readonly PatientAccessOptions _options;
    public DoctorDelegationValidator(IHttpClientFactory clients, IOptions<PatientAccessOptions> options)
    { _clients = clients; _options = options.Value; }

    public async Task<bool> ValidateAsync(DoctorDelegationDto grant, CancellationToken ct)
    {
        if (!Uri.TryCreate(_options.DoctorDelegationValidationUrl, UriKind.Absolute, out var url) ||
            url.Scheme is not ("http" or "https") || _options.DoctorDelegationServiceToken.Length < 32) return false;
        using var client = _clients.CreateClient("doctor-delegation");
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Add("X-Sandbox-Service", _options.DoctorDelegationServiceToken);
        request.Content = JsonContent.Create(grant);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            return response.StatusCode == System.Net.HttpStatusCode.NoContent;
        }
        catch (HttpRequestException) { return false; }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return false; }
    }
}
