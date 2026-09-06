using System.Net;
using System.Net.Http.Headers;
using System.Text;
using AI_WebApi.Model.AITools;
using AI_WebApi.Services.AITools.Rehab;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AI_WebApi.Tests;

public sealed class RehabToolsServiceTests
{
    [Fact]
    public async Task Search_doctors_returns_single_doctor_contract_and_caches_token()
    {
        var loginCalls = 0;
        var queryCalls = 0;
        var handler = new StubHandler(request =>
        {
            if (request.Method == HttpMethod.Post &&
                request.RequestUri?.AbsolutePath == "/login")
            {
                loginCalls++;
                return Json("{\"data\":{\"token\":\"token-1\"}}");
            }

            queryCalls++;
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("space-7", GetQueryValue(request.RequestUri!, "spaceId"));
            Assert.Equal(
                new AuthenticationHeaderValue("Bearer", "token-1"),
                request.Headers.Authorization);
            return Json(
                "{\"message\":\"查询成功\",\"data\":[" +
                "{\"id\":7,\"name\":\"张医生\",\"phone\":\"13800000000\"}]}");
        });
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = new DoctorToolsService(
            new StubHttpClientFactory(handler),
            cache,
            Options.Create(OptionsValue()),
            NullLogger<DoctorToolsService>.Instance);

        var first = await service.SearchDoctorsAsync("space-7");
        var second = await service.SearchDoctorsAsync("space-7");

        Assert.Equal("一个医生", first.Result);
        Assert.Equal("张医生", first.DoctorNames);
        Assert.Equal("7", first.DoctorIds);
        Assert.Equal("13800000000", first.DoctorPhones);
        Assert.Equal("[DoctorRecommend:]13800000000", first.Command);
        Assert.Equal(1, first.Count);
        Assert.Equal(first.Result, second.Result);
        Assert.Equal(1, loginCalls);
        Assert.Equal(2, queryCalls);
    }

    [Fact]
    public async Task Navigate_scene_reads_current_version_and_matches_natural_language()
    {
        var queryCalls = 0;
        var handler = new StubHandler(request =>
        {
            Assert.Equal("key-1", request.Headers.GetValues("apikey").Single());
            Assert.Equal("secret-1", request.Headers.GetValues("apitoken").Single());
            if (request.RequestUri?.AbsolutePath == "/version")
            {
                return Json("{\"data\":321}");
            }

            queryCalls++;
            return queryCalls == 1
                ? Json(
                    "{\"data\":{\"result\":[" +
                    "{\"id\":5,\"name\":\"康复大厅\",\"intro\":\"康复训练入口\"}]}}")
                : Json("{\"data\":{\"result\":[]}}");
        });
        var service = new SceneNavigationService(
            new StubHttpClientFactory(handler),
            Options.Create(OptionsValue()),
            NullLogger<SceneNavigationService>.Instance);

        var result = await service.NavigateAsync("带我去康复大厅", "space-7");

        Assert.Equal("[Telepor:]5", result.Result);
        Assert.Equal("telepor", result.IntentType);
        Assert.Equal("康复大厅", result.MatchName);
        Assert.True(result.Score > 0);
        Assert.Equal(4, queryCalls);
    }

    [Fact]
    public async Task Search_doctors_does_not_mask_upstream_failure_as_empty()
    {
        var handler = new StubHandler(request =>
            request.RequestUri?.AbsolutePath == "/login"
                ? Json("{\"data\":{\"token\":\"token-1\"}}")
                : new HttpResponseMessage(HttpStatusCode.BadGateway)
                {
                    Content = new StringContent("upstream failed")
                });
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = new DoctorToolsService(
            new StubHttpClientFactory(handler),
            cache,
            Options.Create(OptionsValue()),
            NullLogger<DoctorToolsService>.Instance);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.SearchDoctorsAsync("space-7"));

        Assert.Contains("HTTP 502", exception.Message);
    }

    private static RehabToolsOptions OptionsValue() => new()
    {
        DefaultSpaceId = "default-space",
        Doctor = new DoctorLookupOptions
        {
            LoginUrl = "http://upstream.test/login",
            QueryUrl = "http://upstream.test/doctors",
            LoginPhone = "configured-phone",
            LoginPassword = "configured-password",
            TokenTtlMinutes = 60,
            RequestTimeoutSeconds = 10
        },
        Scene = new SceneNavigationOptions
        {
            VersionUrl = "http://upstream.test/version",
            QueryUrl = "http://upstream.test/query",
            ApiKey = "key-1",
            ApiToken = "secret-1",
            RequestTimeoutSeconds = 10
        }
    };

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    private static string GetQueryValue(Uri uri, string key)
    {
        var pair = uri.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(value => value.Split('=', 2))
            .Single(value => Uri.UnescapeDataString(value[0]) == key);
        return Uri.UnescapeDataString(pair[1]);
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _client;

        public StubHttpClientFactory(HttpMessageHandler handler) =>
            _client = new HttpClient(handler, disposeHandler: false);

        public HttpClient CreateClient(string name) => _client;
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) =>
            _handler = handler;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(_handler(request));
    }
}
