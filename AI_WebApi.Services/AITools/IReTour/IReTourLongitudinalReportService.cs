using AI_WebApi.IServices.AITools;
using AI_WebApi.Model.AITools;
using AI_WebApi.Model.Domain.IReGo;
using AI_WebApi.Model.DTOs.AITools;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AI_WebApi.Services.AITools.IReTour;

public sealed class IReTourLongitudinalReportService :
    IIReTourLongitudinalReportService
{
    private readonly IIReTourLongitudinalAnalysisService _analysisService;
    private readonly IIReTourLongitudinalReportHtmlBuilder _htmlBuilder;
    private readonly IIReGoReportImageRenderer _renderer;
    private readonly IIReGoReportArtifactStore _artifacts;
    private readonly IReGoReportImageOptions _imageOptions;
    private readonly IReTourToolOptions _options;
    private readonly ILogger<IReTourLongitudinalReportService> _logger;

    public IReTourLongitudinalReportService(
        IIReTourLongitudinalAnalysisService analysisService,
        IIReTourLongitudinalReportHtmlBuilder htmlBuilder,
        IIReGoReportImageRenderer renderer,
        IIReGoReportArtifactStore artifacts,
        IOptions<IReGoToolOptions> imageOptions,
        IOptions<IReTourToolOptions> options,
        ILogger<IReTourLongitudinalReportService> logger)
    {
        _analysisService = analysisService;
        _htmlBuilder = htmlBuilder;
        _renderer = renderer;
        _artifacts = artifacts;
        _imageOptions = imageOptions.Value.ReportImage;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ToolResponseDto<IReTourLongitudinalReportArtifactDto>>
        GenerateAsync(
            TrustedPatientContext context,
            GenerateIReTourLongitudinalReportRequestDto request,
            string requestBaseUrl,
            string requestId,
            CancellationToken cancellationToken)
    {
        if (!_artifacts.IsConfigured)
        {
            return Failure(
                requestId,
                IReTourReasonCodes.ReportImageSigningKeyNotConfigured,
                ToolStatuses.Failed);
        }

        var analysis = await _analysisService.GetAsync(
            context,
            new GetIReTourLongitudinalAnalysisRequestDto
            {
                SystemContext = request.SystemContext,
                SelectionMode = request.SelectionMode,
                ReportCount = request.ReportCount,
                StartOrdinal = request.StartOrdinal,
                EndOrdinal = request.EndOrdinal,
                StartDate = request.StartDate,
                EndDate = request.EndDate,
                ActivityScope = request.ActivityScope,
                DetailLevel = "standard",
                QualityDetail = "summary"
            },
            requestId,
            cancellationToken);
        if (analysis.Status is ToolStatuses.Failed or ToolStatuses.Unavailable ||
            analysis.Data == null)
        {
            return ToolResponseDto<IReTourLongitudinalReportArtifactDto>.Create(
                "generate_iretour_longitudinal_report",
                requestId,
                analysis.Status,
                null,
                analysis.ReasonCodes,
                analysis.Meta,
                analysis.PatientMessage,
                IReTourContract.Version);
        }

        var snapshot = IReTourReportContentFingerprint.LongitudinalSourceSnapshot(analysis.Data);
        if (request.ExpectedSourceSnapshot != null && request.ExpectedSourceSnapshot != snapshot)
            return Failure(requestId, IReTourReasonCodes.SourceChanged, ToolStatuses.Unavailable,
                analysis.Meta, "训练数据已经变化，请重新查询解读后生成报表。");
        var hasThreePointMetric = analysis.Data.MetricSeries.Any(series =>
            series.ComparisonStatus == "descriptive_same_source_field" &&
            series.Points.Count(point =>
                point.ValueStatus == "valid" && point.Value.HasValue) >= 3);
        if (analysis.Data.Sessions.Count < 3 || !hasThreePointMetric)
        {
            return Failure(
                requestId,
                IReTourReasonCodes.LongitudinalReportInsufficientData,
                ToolStatuses.Unavailable,
                analysis.Meta,
                "同一训练项目至少需要三条有效记录才能生成统合汇报。");
        }

        var artifactRef = _artifacts.CreateArtifactRef(
            analysis.Data.PatientRef,
            IReTourReportContentFingerprint.LongitudinalTrainingScope(analysis.Data),
            IReTourReportContentFingerprint.LongitudinalSourceSnapshot(analysis.Data),
            _options.LongitudinalReportTemplateVersion);
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
                    "IReTour longitudinal report rendering failed. RequestId={RequestId}",
                    requestId);
                return Failure(
                    requestId,
                    IReTourReasonCodes.ReportRenderingUnavailable,
                    ToolStatuses.Failed,
                    analysis.Meta,
                    "统合汇报图片暂时无法生成，请稍后重试。");
            }
        }

        var imageUrl = ImageUrl(requestBaseUrl, artifactRef);
        return ToolResponseDto<IReTourLongitudinalReportArtifactDto>.Create(
            "generate_iretour_longitudinal_report",
            requestId,
            ToolStatuses.Available,
            new IReTourLongitudinalReportArtifactDto
            {
                ArtifactRef = artifactRef,
                SourceSnapshot = snapshot,
                StorageMode = _artifacts.StorageMode,
                ImageUrl = imageUrl,
                DisplayMarkdown = $"![IReTour多次训练报告对比]({imageUrl})",
                ExpiresAt = null,
                TemplateVersion = _options.LongitudinalReportTemplateVersion
            },
            analysis.ReasonCodes,
            analysis.Meta,
            contractVersion: IReTourContract.Version);
    }

    private string ImageUrl(string requestBaseUrl, string artifactRef)
    {
        var configuredBaseUrl = _imageOptions.PublicBaseUrl?.Trim().TrimEnd('/');
        var baseUrl = string.IsNullOrWhiteSpace(configuredBaseUrl)
            ? requestBaseUrl.TrimEnd('/')
            : configuredBaseUrl;
        return $"{baseUrl}/api/ai/patients/report-artifacts/" +
               $"{Uri.EscapeDataString(artifactRef)}/content";
    }

    private ToolResponseDto<IReTourLongitudinalReportArtifactDto> Failure(
        string requestId,
        string reason,
        string status,
        ToolMetaDto? meta = null,
        string? patientMessage = null) =>
        ToolResponseDto<IReTourLongitudinalReportArtifactDto>.Create(
            "generate_iretour_longitudinal_report",
            requestId,
            status,
            null,
            new[] { reason },
            meta ?? IReTourToolMeta.Build(_options),
            patientMessage,
            IReTourContract.Version);
}
