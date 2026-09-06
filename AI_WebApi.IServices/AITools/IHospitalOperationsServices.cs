using AI_WebApi.Model.DTOs.AITools;

namespace AI_WebApi.IServices.AITools;

public interface IHospitalOperationsUpstreamClient
{
    Task<IReadOnlyList<HospitalDirectoryEntryDto>> GetDirectoryAsync(
        CancellationToken cancellationToken = default);

    Task<HospitalOverviewDto> GetOverviewAsync(
        int hospitalId,
        CancellationToken cancellationToken = default);

    Task<HospitalConsultationDto> GetConsultationAsync(
        int hospitalId,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken = default);

    Task<HospitalRoomsDto> GetRoomsAsync(
        int hospitalId,
        CancellationToken cancellationToken = default);

    Task<(HospitalAssessmentTrainingDto AssessmentTraining, HospitalRecordsDto Records)>
        GetDetailsAsync(
            int hospitalId,
            CancellationToken cancellationToken = default);
}

public interface IHospitalOperationsService
{
    Task<HospitalOperationsToolResponseDto> QueryAsync(
        HospitalOperationsQueryRequestDto request,
        string requestBaseUrl,
        string requestId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<HospitalDirectoryEntryDto>> GetDirectoryAsync(
        CancellationToken cancellationToken = default);
}

public interface IHospitalOperationsReportHtmlBuilder
{
    string Build(HospitalOperationsResultDto result);
}

public interface IHospitalOperationsArtifactIdentityProvider
{
    bool IsConfigured { get; }

    HospitalOperationsArtifactIdentityDto Create(
        HospitalOperationsResultDto result);
}

public interface IHospitalOperationsArtifactStore
{
    string StorageMode { get; }

    bool TryGet(
        string artifactRef,
        out HospitalOperationsArtifactManifestDto manifest);

    HospitalOperationsArtifactWriteResult Store(
        HospitalOperationsArtifactManifestDto manifest,
        string html,
        byte[] png);

    HospitalReportAccessResult Resolve(
        string artifactRef,
        string format);
}
