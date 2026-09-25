using AI_WebApi.Model.Domain.IReGo;
using AI_WebApi.Model.Domain.IReTour;
using AI_WebApi.Model.Entities;

namespace AI_WebApi.Repository.AITools;

/// <summary>
/// Raw IReTour data set for one trusted patient: patient record, verified
/// bindings, walk sessions, devices and report rows. Report-link classification
/// happens in the service layer against this bounded set.
/// </summary>
public sealed class IReTourRawDataSet
{
    public IReTourPatientEntity? Patient { get; init; }
    public IReTourPatientBinding Binding { get; init; } = new() { Verified = false };
    public IReadOnlyList<int> SourceUserIds { get; init; } = Array.Empty<int>();
    public IReadOnlyList<IReTourWalkEntity> Walks { get; init; } =
        Array.Empty<IReTourWalkEntity>();
    public IReadOnlyDictionary<int, IReTourDeviceEntity> Devices { get; init; } =
        new Dictionary<int, IReTourDeviceEntity>();
    public IReadOnlyList<IReTourSummaryReportEntity> SummaryReports { get; init; } =
        Array.Empty<IReTourSummaryReportEntity>();
    public IReadOnlyList<IReTourRawReportEntity> RawReports { get; init; } =
        Array.Empty<IReTourRawReportEntity>();
    public IReadOnlyList<IReTourCognitiveReportEntity> CognitiveReports { get; init; } =
        Array.Empty<IReTourCognitiveReportEntity>();
}

public interface IIReTourDataRepository
{
    Task<IReTourRawDataSet> LoadPatientDataAsync(
        TrustedPatientContext context,
        CancellationToken cancellationToken);
}
