using AI_WebApi.Model.Domain.IReGo;
using AI_WebApi.Model.DTOs.AITools;

namespace AI_WebApi.IServices.AITools;

public interface IAssessmentToolService
{
    Task<ToolResponseDto<AssessmentToolDataDto>> ReadAsync(string operation, TrustedPatientContext context,
        AssessmentToolRequestDto request, string requestId, CancellationToken cancellationToken);
    Task<ToolResponseDto<AssessmentReportArtifactDto>> ReportAsync(bool longitudinal, TrustedPatientContext context,
        AssessmentToolRequestDto request, string baseUrl, string requestId, CancellationToken cancellationToken);
}
