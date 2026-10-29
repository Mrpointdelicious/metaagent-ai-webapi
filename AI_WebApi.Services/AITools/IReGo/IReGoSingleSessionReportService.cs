using AI_WebApi.IServices.AITools;
using AI_WebApi.Model.AITools;
using AI_WebApi.Model.Domain.IReGo;
using AI_WebApi.Model.DTOs.AITools;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AI_WebApi.Services.AITools.IReGo;

public sealed class IReGoSingleSessionReportService :
    IIReGoSingleSessionReportService
{
    private readonly IIReGoSessionAnalysisService _analysisService;
    private readonly IIReGoSingleSessionReportHtmlBuilder _htmlBuilder;
    private readonly IIReGoReportImageRenderer _renderer;
    private readonly IIReGoReportArtifactStore _artifacts;
    private readonly IReGoReportImageOptions _options;
    private readonly ILogger<IReGoSingleSessionReportService> _logger;

    public IReGoSingleSessionReportService(
        IIReGoSessionAnalysisService analysisService,
        IIReGoSingleSessionReportHtmlBuilder htmlBuilder,
        IIReGoReportImageRenderer renderer,
        IIReGoReportArtifactStore artifacts,
        IOptions<IReGoToolOptions> options,
        ILogger<IReGoSingleSessionReportService> logger)
    {
        _analysisService = analysisService;
        _htmlBuilder = htmlBuilder;
        _renderer = renderer;
        _artifacts = artifacts;
        _options = options.Value.ReportImage;
        _logger = logger;
    }

    public async Task<ToolResponseDto<IReGoSingleSessionReportArtifactDto>>
        GenerateAsync(
            TrustedPatientContext context,
            GenerateIReGoSingleSessionReportRequestDto request,
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
            new GetIReGoSessionAnalysisRequestDto
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
            return ToolResponseDto<IReGoSingleSessionReportArtifactDto>.Create(
                "generate_irego_single_session_report",
                requestId,
                analysis.Status,
                null,
                analysis.ReasonCodes,
                analysis.Meta,
                analysis.PatientMessage);
        }

        if (analysis.Data.Time.TrainingState != "completed")
        {
            return Failure(
                requestId,
                IReGoReasonCodes.SingleReportRequiresCompletedSession,
                ToolStatuses.Unavailable,
                analysis.Meta,
                "本次训练尚不能确认完整完成，暂不生成单次训练报告。");
        }

        if (analysis.Data.ReportBlocks.Count == 0)
        {
            return Failure(
                requestId,
                IReGoReasonCodes.SingleReportHasNoBlocks,
                ToolStatuses.Unavailable,
                analysis.Meta,
                "本次训练没有可用于生成报告的训练项目记录。");
        }

        var evaluation = IReGoSingleSessionReportEvaluation.CreateBackend(
            analysis.Data);
        if (!evaluation.IsValid)
        {
            return Failure(
                requestId,
                evaluation.ReasonCode ??
                    IReGoReasonCodes.SingleReportAiEvaluationInvalid,
                ToolStatuses.Failed,
                analysis.Meta,
                "后端无法根据本次训练事实生成评价，请稍后重试。");
        }

        if (context.Access != null && request.ExpectedSourceSnapshot != analysis.Data.SourceSnapshot)
            return Failure(requestId, "IREGO_SOURCE_CHANGED", ToolStatuses.Unavailable, analysis.Meta, "康复记录已更新，请重新获取分析后生成报告。");

        AI_WebApi.Services.AITools.PatientContext.RecordTenantPresentation.Annotate(analysis.Data, context.Access);

        var artifactRef = _artifacts.CreateArtifactRef(
            analysis.Data.Session.PatientRef,
            IReGoReportContentFingerprint.SingleTrainingScope(analysis.Data),
            IReGoReportContentFingerprint.SingleSourceSnapshot(
                analysis.Data,
                evaluation),
            _options.TemplateVersion);

        if (!_artifacts.TryGet(artifactRef, out _))
        {
            try
            {
                var html = _htmlBuilder.Build(
                    analysis.Data,
                    evaluation.Presentation);
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
                    "IREGO single-session report rendering failed. RequestId={RequestId}",
                    requestId);
                return Failure(
                    requestId,
                    IReGoReasonCodes.ReportRenderingUnavailable,
                    ToolStatuses.Failed,
                    analysis.Meta,
                    "报告图片暂时无法生成，请稍后重试。");
            }
        }

        var configuredBaseUrl = _options.PublicBaseUrl?.Trim().TrimEnd('/');
        var baseUrl = string.IsNullOrWhiteSpace(configuredBaseUrl)
            ? requestBaseUrl.TrimEnd('/')
            : configuredBaseUrl;
        var imageUrl = $"{baseUrl}/api/ai/patients/report-artifacts/" +
                       $"{Uri.EscapeDataString(artifactRef)}/content";

        return ToolResponseDto<IReGoSingleSessionReportArtifactDto>.Create(
            "generate_irego_single_session_report",
            requestId,
            ToolStatuses.Available,
            new IReGoSingleSessionReportArtifactDto
            {
                SourceSnapshot = analysis.Data.SourceSnapshot,
                TenantSegments = AI_WebApi.Services.AITools.PatientContext.RecordTenantPresentation.Segments(analysis.Data, context.Access),
                ArtifactRef = artifactRef,
                StorageMode = _artifacts.StorageMode,
                ImageUrl = imageUrl,
                DisplayMarkdown = $"![单次训练报告]({imageUrl})",
                ExpiresAt = null,
                TemplateVersion = _options.TemplateVersion
            },
            Array.Empty<string>(),
            new ToolMetaDto
            {
                GeneratedAt = DateTimeOffset.UtcNow,
                SourceCompleteness = analysis.Meta.SourceCompleteness,
                Watermark = analysis.Meta.Watermark
            });
    }

    public ReportImageAccessResult ResolveImage(string token) =>
        _artifacts.Resolve(token);

    public ReportImageAccessResult ResolveArtifact(string artifactRef) =>
        _artifacts.ResolveArtifact(artifactRef);

    private static ToolResponseDto<IReGoSingleSessionReportArtifactDto> Failure(
        string requestId,
        string reason,
        string status,
        ToolMetaDto? meta = null,
        string? patientMessage = null) =>
        ToolResponseDto<IReGoSingleSessionReportArtifactDto>.Create(
            "generate_irego_single_session_report",
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
