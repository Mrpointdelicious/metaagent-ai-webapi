using AI_WebApi.IServices.AITools;
using AI_WebApi.Model.AITools;
using AI_WebApi.Model.DTOs.AITools;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AI_WebApi.Services.AITools.Rehab;

public sealed class SceneNavigationService : ISceneNavigationService
{
    private readonly SceneCommandCatalog _catalog;
    private readonly SceneCommandResolver _resolver;
    private readonly ILogger<SceneNavigationService> _logger;

    public SceneNavigationService(
        IHttpClientFactory httpClientFactory,
        IOptions<RehabToolsOptions> options,
        ILogger<SceneNavigationService> logger)
    {
        _catalog = new SceneCommandCatalog(httpClientFactory, options.Value);
        _resolver = new SceneCommandResolver(options.Value.Scene.Resolver);
        _logger = logger;
    }

    public async Task<SceneOperationToolResponseDto> NavigateAsync(
        string target,
        string? spaceId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(target))
            throw new ArgumentException("Target is required.", nameof(target));

        SceneCommandCatalogSnapshot catalog;
        try
        {
            catalog = await _catalog.LoadAsync(spaceId, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // A partial catalog cannot establish that a command is unique.
            _logger.LogWarning(exception, "Scene command catalog could not be loaded.");
            throw;
        }

        return new SceneOperationToolResponseDto
        {
            SpaceId = catalog.SpaceId,
            Version = catalog.Version,
            Commands = _resolver.Resolve(target, catalog.Commands, cancellationToken)
        };
    }
}
