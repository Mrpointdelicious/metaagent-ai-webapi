using AI_WebApi.Controllers;
using AI_WebApi.IServices.AITools;
using AI_WebApi.Model;
using AI_WebApi.Model.AITools;
using AI_WebApi.Model.DTOs.AITools;
using AI_WebApi.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AI_WebApi.Tests;

public sealed class SceneHttpBoundaryTests
{
    [Theory]
    [InlineData(null, true, 200, 1)]
    [InlineData(321, true, 200, 1)]
    [InlineData(322, true, 409, 1)]
    [InlineData(0, true, 409, 1)]
    [InlineData(-1, true, 400, 0)]
    [InlineData(321, false, 401, 0)]
    public async Task Version_guard_preserves_envelope_and_never_returns_stale_actions(
        int? expected, bool authorized, int status, int calls)
    {
        var scene = new SceneStub();
        var controller = Controller(scene, authorized);
        var response = await controller.NavigateScene(new SceneOperationToolRequestDto
        {
            Target = "关闭地图", SpaceId = "space-7", ExpectedSceneVersion = expected
        }, CancellationToken.None);

        var http = Assert.IsAssignableFrom<ObjectResult>(response.Result);
        Assert.Equal(status, http.StatusCode);
        var body = Assert.IsType<ApiResponse<SceneOperationToolResponseDto>>(http.Value);
        Assert.Equal(status, body.Status);
        Assert.Equal(calls, scene.Calls);
        if (status != 200) Assert.Null(body.Data);
        else
        {
            Assert.Equal("[ClickPoint:]22", body.Data!.Result);
            Assert.Equal(body.Data.Result, Assert.Single(body.Data.Commands).Result);
        }
    }

    [Fact]
    public async Task Catalog_failure_does_not_return_actions()
    {
        var response = await Controller(new SceneStub { Fail = true }, true).NavigateScene(
            new SceneOperationToolRequestDto { Target = "关闭地图" }, CancellationToken.None);
        var http = Assert.IsType<ObjectResult>(response.Result);
        Assert.Equal(502, http.StatusCode);
        Assert.Null(Assert.IsType<ApiResponse<SceneOperationToolResponseDto>>(http.Value).Data);
    }

    [Fact]
    public async Task Cancellation_propagates_to_catalog()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Controller(new SceneStub(), true).NavigateScene(
                new SceneOperationToolRequestDto { Target = "关闭地图" }, cancellation.Token));
    }

    private static AiRehabToolsController Controller(SceneStub scene, bool authorized)
    {
        var context = new DefaultHttpContext();
        if (authorized) context.Request.Headers.Authorization = "Bearer synthetic-service";
        return new AiRehabToolsController(null!, scene,
            new DifyServiceRequestAuthenticator(Options.Create(new IReGoToolOptions
            {
                DifyServiceBearerToken = "synthetic-service"
            })), NullLogger<AiRehabToolsController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = context }
        };
    }

    private sealed class SceneStub : ISceneNavigationService
    {
        public int Calls { get; private set; }
        public bool Fail { get; init; }
        public Task<SceneOperationToolResponseDto> NavigateAsync(
            string target, string? spaceId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            if (Fail) throw new InvalidOperationException("synthetic catalog failure");
            return Task.FromResult(new SceneOperationToolResponseDto
            {
                SpaceId = spaceId ?? "space-7", Version = 321,
                Commands = new[] { new SceneCommandResultDto
                {
                    Order = 1, SourceText = target, Status = "matched", Result = "[ClickPoint:]22",
                    MatchName = "关闭地图", IntentType = "click_point", Score = 1
                }}
            });
        }
    }
}
