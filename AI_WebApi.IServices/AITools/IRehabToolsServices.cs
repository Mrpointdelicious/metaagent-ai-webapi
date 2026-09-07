using AI_WebApi.Model.DTOs.AITools;

namespace AI_WebApi.IServices.AITools;

public interface IDoctorToolsService
{
    Task<SearchDoctorsToolResponseDto> SearchDoctorsAsync(
        string? spaceId,
        CancellationToken cancellationToken = default);
}

public interface ISceneNavigationService
{
    /// <summary>Resolves ordered command spans against one complete current-space catalog.</summary>
    Task<SceneOperationToolResponseDto> NavigateAsync(
        string target,
        string? spaceId,
        CancellationToken cancellationToken = default);
}
