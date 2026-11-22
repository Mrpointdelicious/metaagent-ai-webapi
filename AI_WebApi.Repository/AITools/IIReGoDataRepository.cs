using AI_WebApi.Model.Domain.IReGo;

namespace AI_WebApi.Repository.AITools;

public interface IIReGoDataRepository
{
    Task<IReGoRawDataSet> LoadPatientDataAsync(TrustedPatientContext context, CancellationToken cancellationToken = default);
}
