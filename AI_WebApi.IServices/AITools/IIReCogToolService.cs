using AI_WebApi.Model.Domain.IReGo;
using AI_WebApi.Model.DTOs.AITools;

namespace AI_WebApi.IServices.AITools;

public interface IIReCogToolService
{
    Task<ToolResponseDto<IReCogAnalysisDto>> ReadAsync(string operation, TrustedPatientContext context,
        IReCogRequestDto request, string requestId, CancellationToken cancellationToken);
    Task<ToolResponseDto<IReTourSingleSessionReportArtifactDto>> ReportAsync(bool longitudinal, TrustedPatientContext context,
        IReCogRequestDto request, string baseUrl, string requestId, CancellationToken cancellationToken);
}
