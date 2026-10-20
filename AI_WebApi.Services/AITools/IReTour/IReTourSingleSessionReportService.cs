using AI_WebApi.IServices.AITools;
using AI_WebApi.Model.AITools;
using AI_WebApi.Model.Domain.IReGo;
using AI_WebApi.Model.DTOs.AITools;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AI_WebApi.Services.AITools.IReTour;

public sealed class IReTourSingleSessionReportService :
    IIReTourSingleSessionReportService
{
    private readonly IIReTourSessionAnalysisService _analysisService;
    private readonly IIReTourSingleSessionReportHtmlBuilder _htmlBuilder;
    private readonly IIReGoReportImageRenderer _renderer;
    private readonly IIReGoReportArtifactStore _artifacts;
    private readonly IReGoReportImageOptions _imageOptions;
    private readonly IReTourToolOptions _options;
    private readonly ILogger<IReTourSingleSessionReportService> _logger;

    public IReTourSingleSessionReportService(
        IIReTourSessionAnalysisService analysisService,
        IIReTourSingleSessionReportHtmlBuilder htmlBuilder,
        IIReGoReportImageRenderer renderer,
        IIReGoReportArtifactStore artifacts,
        IOptions<IReGoToolOptions> imageOptions,
        IOptions<IReTourToolOptions> options,
        ILogger<IReTourSingleSessionReportService> logger)
    {
        _analysisService = analysisService;
        _htmlBuilder = htmlBuilder;
        _renderer = renderer;
        _artifacts = artifacts;
        _imageOptions = imageOptions.Value.ReportImage;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ToolResponseDto<IReTourSingleSessionReportArtifactDto>>
        GenerateAsync(
            TrustedPatientContext context,
            GenerateIReTourSingleSessionReportRequestDto request,
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
            new GetIReTourSessionAnalysisRequestDto
            {
                SystemContext = request.SystemContext,
                Selector = "session_ref",
                SessionRef = request.SessionRef,
                DetailLevel = "standard",
                QualityDetail = "summary"
            },
            requestId,
            cancellationToken);
        if (analysis.Status is ToolStatuses.Failed or ToolStatuses.Unavailable ||
            analysis.Data == null)
        {
            return ToolResponseDto<IReTourSingleSessionReportArtifactDto>.Create(
                "generate_iretour_single_session_report",
                requestId,
                analysis.Status,
                null,
                analysis.ReasonCodes,
                analysis.Meta,
                analysis.PatientMessage,
                IReTourContract.Version);
        }

        if (analysis.Data.Time.TrainingState != "completed")
        {
            return Failure(
                requestId,
                IReTourReasonCodes.SingleReportRequiresCompletedSession,
                ToolStatuses.Unavailable,
                analysis.Meta,
                "本次训练尚不能确认完整完成，暂不生成单次训练报告。");
        }
        if (!analysis.Data.Quality.Usable)
        {
            return Failure(
                requestId,
                IReTourReasonCodes.SingleReportQualityBlocked,
                ToolStatuses.Unavailable,
                analysis.Meta,
                "本次训练记录存在质量问题，暂不生成单次训练报告。");
        }
        if (analysis.Data.Result.ResultState != "summary_available" ||
            !analysis.Data.Result.Metrics.Any(x =>
                x.ValueStatus == "valid" && x.NormalizedValue.HasValue))
        {
            return Failure(
                requestId,
                IReTourReasonCodes.SingleReportHasNoMetrics,
                ToolStatuses.Unavailable,
                analysis.Meta,
                "本次训练没有可用于生成报告的有效汇总指标。");
        }

        var snapshot = IReTourReportContentFingerprint.SingleSourceSnapshot(analysis.Data);
        if (request.ExpectedSourceSnapshot != null && request.ExpectedSourceSnapshot != snapshot)
            return Failure(requestId, IReTourReasonCodes.SourceChanged, ToolStatuses.Unavailable,
                analysis.Meta, "训练数据已经变化，请重新查询解读后生成报表。");
        var artifactRef = _artifacts.CreateArtifactRef(
            analysis.Data.Session.PatientRef,
            IReTourReportContentFingerprint.SingleTrainingScope(analysis.Data),
            IReTourReportContentFingerprint.SingleSourceSnapshot(analysis.Data),
            _options.SingleReportTemplateVersion);
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
                    "IReTour single-session report rendering failed. RequestId={RequestId}",
                    requestId);
                return Failure(
                    requestId,
                    IReTourReasonCodes.ReportRenderingUnavailable,
                    ToolStatuses.Failed,
                    analysis.Meta,
                    "报告图片暂时无法生成，请稍后重试。");
            }
        }

        var imageUrl = ImageUrl(requestBaseUrl, artifactRef);
        return ToolResponseDto<IReTourSingleSessionReportArtifactDto>.Create(
            "generate_iretour_single_session_report",
            requestId,
            ToolStatuses.Available,
            new IReTourSingleSessionReportArtifactDto
            {
                ArtifactRef = artifactRef,
                SourceSnapshot = snapshot,
                StorageMode = _artifacts.StorageMode,
                ImageUrl = imageUrl,
                DisplayMarkdown = $"![IReTour单次训练报告]({imageUrl})",
                ExpiresAt = null,
                TemplateVersion = _options.SingleReportTemplateVersion
            },
            Array.Empty<string>(),
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

    private ToolResponseDto<IReTourSingleSessionReportArtifactDto> Failure(
        string requestId,
        string reason,
        string status,
        ToolMetaDto? meta = null,
        string? patientMessage = null) =>
        ToolResponseDto<IReTourSingleSessionReportArtifactDto>.Create(
            "generate_iretour_single_session_report",
            requestId,
            status,
            null,
            new[] { reason },
            meta ?? IReTourToolMeta.Build(_options),
            patientMessage,
            IReTourContract.Version);
}
