using AI_WebApi.IServices.AITools;
using AI_WebApi.Model.AITools;
using AI_WebApi.Model.Domain.IReGo;
using AI_WebApi.Model.DTOs.AITools;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AI_WebApi.Services.AITools.IReGo;

public sealed class IReGoLongitudinalReportService :
    IIReGoLongitudinalReportService
{
    private readonly IIReGoLongitudinalAnalysisService _analysisService;
    private readonly IIReGoLongitudinalReportHtmlBuilder _htmlBuilder;
    private readonly IIReGoReportImageRenderer _renderer;
    private readonly IIReGoReportArtifactStore _artifacts;
    private readonly IReGoReportImageOptions _options;
    private readonly ILogger<IReGoLongitudinalReportService> _logger;

    public IReGoLongitudinalReportService(
        IIReGoLongitudinalAnalysisService analysisService,
        IIReGoLongitudinalReportHtmlBuilder htmlBuilder,
        IIReGoReportImageRenderer renderer,
        IIReGoReportArtifactStore artifacts,
        IOptions<IReGoToolOptions> options,
        ILogger<IReGoLongitudinalReportService> logger)
    {
        _analysisService = analysisService;
        _htmlBuilder = htmlBuilder;
        _renderer = renderer;
        _artifacts = artifacts;
        _options = options.Value.ReportImage;
        _logger = logger;
    }

    public async Task<ToolResponseDto<IReGoLongitudinalReportArtifactDto>>
        GenerateAsync(
            TrustedPatientContext context,
            GenerateIReGoLongitudinalReportRequestDto request,
            string requestBaseUrl,
            string requestId,
            CancellationToken cancellationToken = default)
    {
        if (!_artifacts.IsConfigured)
        {
            return Failure(
                requestId,
                IReGoReasonCodes.ReportImageSigningKeyNotConfigured,
                ToolStatuses.Failed);
        }

        var analysis = await _analysisService.GetAsync(
            context,
            new GetIReGoLongitudinalAnalysisRequestDto
            {
                SystemContext = request.SystemContext,
                SelectionMode = request.SelectionMode,
                StartOrdinal = request.StartOrdinal,
                EndOrdinal = request.EndOrdinal,
                StartDate = request.StartDate,
                EndDate = request.EndDate,
                Selector = request.Selector,
                ReportCount = request.ReportCount,
                SessionRefs = request.SessionRefs,
                ProjectScope = request.ProjectScope,
                DetailLevel = "standard",
                QualityDetail = "summary"
            },
            requestId,
            cancellationToken);
        if (analysis.Status is ToolStatuses.Failed or ToolStatuses.Unavailable ||
            analysis.Data == null)
        {
            return ToolResponseDto<IReGoLongitudinalReportArtifactDto>.Create(
                "generate_irego_longitudinal_report",
                requestId,
                analysis.Status,
                null,
                analysis.ReasonCodes,
                analysis.Meta,
                analysis.PatientMessage);
        }

        if (analysis.Data.Window.CompleteReportCount < 2)
        {
            return Failure(
                requestId,
                IReGoReasonCodes.LongitudinalRequiresTwoReports,
                ToolStatuses.Unavailable,
                analysis.Meta,
                "所选连续报告范围内至少需要两份完整报告才能生成对比报表。");
        }

        var hasThreePointSubitem = analysis.Data.Projects
            .SelectMany(x => x.Lanes)
            .SelectMany(x => x.MetricSeries)
            .Any(x => x.Points.Count(point =>
                point.Status == "valid" &&
                point.NormalizedValue.HasValue) >= 3);
        if (!hasThreePointSubitem)
        {
            return Failure(
                requestId,
                IReGoReasonCodes.LongitudinalEvaluationInsufficientData,
                ToolStatuses.Unavailable,
                analysis.Meta,
                "可比训练记录不足3条，暂时无法生成统合评价报告。");
        }

        if (context.Access != null && request.ExpectedSourceSnapshot != analysis.Data.SourceSnapshot)
            return Failure(requestId, "IREGO_SOURCE_CHANGED", ToolStatuses.Unavailable, analysis.Meta, "康复记录已更新，请重新获取分析后生成报告。");

        AI_WebApi.Services.AITools.PatientContext.RecordTenantPresentation.Annotate(analysis.Data, context.Access);

        var artifactRef = _artifacts.CreateArtifactRef(
            analysis.Data.PatientRef,
            IReGoReportContentFingerprint.LongitudinalTrainingScope(
                analysis.Data),
            IReGoReportContentFingerprint.LongitudinalSourceSnapshot(
                analysis.Data),
            _options.LongitudinalTemplateVersion);
        if (!_artifacts.TryGet(artifactRef, out _))
        {
            try
            {
                var html = _htmlBuilder.Build(analysis.Data);
                var png = await _renderer.RenderPngAsync(html, cancellationToken);
                _artifacts.Store(artifactRef, png);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    exception,
                    "IREGO longitudinal report rendering failed. RequestId={RequestId}",
                    requestId);
                return Failure(
                    requestId,
                    IReGoReasonCodes.ReportRenderingUnavailable,
                    ToolStatuses.Failed,
                    analysis.Meta,
                    "跨报告图片暂时无法生成，请稍后重试。");
            }
        }

        var configuredBaseUrl = _options.PublicBaseUrl?.Trim().TrimEnd('/');
        var baseUrl = string.IsNullOrWhiteSpace(configuredBaseUrl)
            ? requestBaseUrl.TrimEnd('/')
            : configuredBaseUrl;
        var imageUrl = $"{baseUrl}/api/ai/patients/report-artifacts/" +
                       $"{Uri.EscapeDataString(artifactRef)}/content";

        return ToolResponseDto<IReGoLongitudinalReportArtifactDto>.Create(
            "generate_irego_longitudinal_report",
            requestId,
            ToolStatuses.Available,
            new IReGoLongitudinalReportArtifactDto
            {
                SourceSnapshot = analysis.Data.SourceSnapshot,
                TenantSegments = AI_WebApi.Services.AITools.PatientContext.RecordTenantPresentation.Segments(analysis.Data, context.Access),
                ArtifactRef = artifactRef,
                StorageMode = _artifacts.StorageMode,
                ImageUrl = imageUrl,
                DisplayMarkdown = $"![多次训练报告对比]({imageUrl})",
                ExpiresAt = null,
                TemplateVersion = _options.LongitudinalTemplateVersion
            },
            analysis.ReasonCodes,
            analysis.Meta);
    }

    private static ToolResponseDto<IReGoLongitudinalReportArtifactDto> Failure(
        string requestId,
        string reason,
        string status,
        ToolMetaDto? meta = null,
        string? patientMessage = null) =>
        ToolResponseDto<IReGoLongitudinalReportArtifactDto>.Create(
            "generate_irego_longitudinal_report",
            requestId,
            status,
            null,
            new[] { reason },
            meta ?? new ToolMetaDto
            {
                SourceCompleteness = "authoritative_primary"
            },
            patientMessage);
}
