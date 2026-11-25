using System.Net;
using System.Text.Json;
using AI_WebApi.Controllers;
using AI_WebApi.Repository.AITools;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

namespace AI_WebApi.Tests;

public sealed class PatientSelfSummaryTests
{
    [Theory]
    [InlineData("hospital-a", 200, 2)]
    [InlineData("hospital-b", 200, 1)]
    [InlineData("no-membership", 403, -1)]
    [InlineData("inactive", 403, -1)]
    [InlineData("expired", 401, -1)]
    [InlineData("offline", 503, -1)]
    [InlineData("spoof", 400, -1)]
    [InlineData("", 401, -1)]
    public async Task UsesOnlyVerifiedSessionAndCurrentMembership(string token, int status, int count)
    {
        using var db = new PatientAccessTestDatabase();
        var factory = new SessionFactory();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["PatientSelf:SessionUrl"] = "https://gateway.invalid/me" }).Build();
        var accounts = new Lazy<IPatientAccessRepository>(() => new PatientAccessRepository(db));
        var reports = new Lazy<IPatientSelfReportCount>(() => new PatientSelfReportCount(db));
        var controller = new PatientSelfController(config, factory, accounts, reports)
            { ControllerContext = new() { HttpContext = new DefaultHttpContext() } };
        if (token != "") controller.Request.Headers["Authorization"] = "Bearer " + token;
        if (token == "spoof") controller.Request.QueryString = new QueryString("?patient_id=9");
        var result = await controller.RehabSummary(default);
        if (count < 0) Assert.Equal(status, Assert.IsAssignableFrom<StatusCodeResult>(result).StatusCode);
        else Assert.Equal("{\"record_count\":" + count + "}", JsonSerializer.Serialize(Assert.IsType<OkObjectResult>(result).Value));
        Assert.Equal(token is "" or "spoof" ? 0 : 1, factory.Calls);
        Assert.Equal(token is "hospital-a" or "hospital-b" or "no-membership", accounts.IsValueCreated);
        Assert.Equal(count >= 0, reports.IsValueCreated);
    }

    [Theory]
    [InlineData(null, 401)]
    [InlineData("Bearer synthetic", 503)]
    public async Task MissingConfigurationDoesNotActivateRepositories(string? header, int expected)
    {
        var activations = 0;
        var factory = new SessionFactory();
        var controller = new PatientSelfController(new ConfigurationBuilder().Build(), factory,
            new(() => { activations++; throw new InvalidOperationException(); }),
            new(() => { activations++; throw new InvalidOperationException(); }))
            { ControllerContext = new() { HttpContext = new DefaultHttpContext() } };
        if (header != null) controller.Request.Headers["Authorization"] = header;
        var result = await controller.RehabSummary(default);
        Assert.Equal(expected, Assert.IsAssignableFrom<StatusCodeResult>(result).StatusCode);
        Assert.Equal(0, activations);
        Assert.Equal(0, factory.Calls);
    }

    private sealed class SessionFactory : HttpMessageHandler, IHttpClientFactory
    {
        public int Calls { get; private set; }
        public HttpClient CreateClient(string name) => new(this, false);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("https://gateway.invalid/me", request.RequestUri!.AbsoluteUri);
            var token = request.Headers.Authorization!.Parameter;
            var status = token switch { "expired" => HttpStatusCode.Unauthorized, "offline" => HttpStatusCode.ServiceUnavailable, _ => HttpStatusCode.OK };
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(JsonSerializer.Serialize(new
                { active = token != "inactive", login_name = token == "no-membership" ? "000009" : "000007", current_business_id = token == "hospital-b" ? 2 : 1 })) });
        }
    }
}
