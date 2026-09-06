using AI_WebApi.Model.Domain.IReGo;
using AI_WebApi.Model.DTOs.AITools;

namespace AI_WebApi.IServices.AITools;

public interface IIReGoReportParser
{
    ParsedIReGoReport Parse(string? reportDetails);
}

public interface IIReGoPatientEvaluationService
{
    Task<IReGoPatientEvaluation> EvaluateAsync(
        TrustedPatientContext context,
        CancellationToken cancellationToken = default);
}

public interface IIReGoSessionRefProvider
{
    bool IsConfigured { get; }

    string Create(
        int? tenantId,
        int patientId,
        int planId,
        int reportId,
        string schemaSignature);

    string CreatePlan(
        int? tenantId,
        int patientId,
        int planId);

    string CreatePatient(int? tenantId, int patientId);

    string CreateTenant(int tenantId);

    bool Matches(
        string sessionRef,
        int? tenantId,
        int patientId,
        int planId,
        int reportId,
        string schemaSignature);
}

public interface IPatientContextSnapshotCache
{
    bool TryGetSnapshot(string contextKey, out PatientContextSnapshot snapshot);
    void Store(PatientContextSnapshot snapshot);
    void StoreSession(IReGoSessionBinding binding);
    bool TryResolveSession(
        string contextKey,
        string sessionRef,
        out IReGoSessionBinding binding);
}

public interface IMultisourcePatientContextService
{
    Task<ToolResponseDto<MultisourcePatientContextDto>> GetAsync(
        TrustedPatientContext context,
        GetMultisourcePatientContextRequestDto request,
        string requestId,
        CancellationToken cancellationToken = default);
}

public interface IIReGoSessionAnalysisService
{
    Task<ToolResponseDto<IReGoSessionAnalysisDto>> GetAsync(
        TrustedPatientContext context,
        GetIReGoSessionAnalysisRequestDto request,
        string requestId,
        CancellationToken cancellationToken = default);
}

public interface IIReGoPatientHistoryService
{
    Task<ToolResponseDto<IReGoPatientHistoryDto>> GetAsync(
        TrustedPatientContext context,
        GetIReGoPatientHistoryRequestDto request,
        string requestId,
        CancellationToken cancellationToken = default);
}

public interface IIReGoLongitudinalAnalysisService
{
    Task<ToolResponseDto<IReGoLongitudinalAnalysisDto>> GetAsync(
        TrustedPatientContext context,
        GetIReGoLongitudinalAnalysisRequestDto request,
        string requestId,
        CancellationToken cancellationToken = default);
}

public interface IIReGoThresholdRegistry
{
    string Version { get; }

    IReGoMetricThresholdDto? Resolve(
        string metricCode,
        string? unit,
        IReadOnlyDictionary<string, decimal?> conditions);
}

public interface IIReGoStrokeCapabilityMapper
{
    string Version { get; }
    IReGoCapabilityDefinition? Resolve(string metricCode);
}

public interface IIReGoPatientMessageComposer
{
    string Version { get; }

    string ComposeMetric(
        string verdict,
        string metricLabel,
        IReGoCapabilityDefinition capability,
        string betterDirection);

    string ComposeCapability(
        string verdict,
        IReGoCapabilityDefinition capability);

    string ComposeMetricExplanation(
        string verdict,
        string metricLabel,
        decimal baseline,
        decimal current,
        decimal delta,
        IReGoMetricThresholdDto threshold);

    string ComposeCapabilityExplanation(
        string verdict,
        IReGoCapabilityDefinition capability,
        IReadOnlyList<string> evidenceExplanations);

    string ComposeLifeHint(
        string verdict,
        IReGoCapabilityDefinition capability);

    IReGoLayeredPatientMessage ComposeGame(
        string verdict,
        string comparisonMode,
        string displayName,
        int baselineDifficulty,
        int currentDifficulty,
        decimal baselineRate,
        decimal currentRate,
        decimal threshold,
        bool currentComplete);
}

public interface IIReGoEvaluationRuleEngine
{
    string Version { get; }

    IReGoLongitudinalInterpretationDto EvaluateMetric(
        IReGoMetricEvaluationInput input);

    IReadOnlyList<IReGoCapabilityEvaluationDto> AggregateCapabilities(
        IEnumerable<IReGoLongitudinalInterpretationDto> interpretations);

    IReGoCapabilityEvaluationDto? EvaluateTrainingMode(
        IReGoTrainingModeEvaluationInput input);

    IReGoCapabilityEvaluationDto? EvaluateGamePerformance(
        IReGoGamePerformanceEvaluationInput input);
}

public interface IIReGoLongitudinalReportService
{
    Task<ToolResponseDto<IReGoLongitudinalReportArtifactDto>> GenerateAsync(
        TrustedPatientContext context,
        GenerateIReGoLongitudinalReportRequestDto request,
        string requestBaseUrl,
        string requestId,
        CancellationToken cancellationToken = default);
}

public interface IIReGoLongitudinalReportHtmlBuilder
{
    string Build(IReGoLongitudinalAnalysisDto analysis);
}

public interface IIReGoSingleSessionReportService
{
    Task<ToolResponseDto<IReGoSingleSessionReportArtifactDto>> GenerateAsync(
        TrustedPatientContext context,
        GenerateIReGoSingleSessionReportRequestDto request,
        string requestBaseUrl,
        string requestId,
        CancellationToken cancellationToken = default);

    ReportImageAccessResult ResolveImage(string token);
    ReportImageAccessResult ResolveArtifact(string artifactRef);
}

public interface IIReGoSingleSessionReportHtmlBuilder
{
    string Build(
        IReGoSessionAnalysisDto analysis,
        IReGoSingleSessionAiEvaluationDto aiEvaluation);
}

public interface IIReGoReportImageRenderer
{
    Task<byte[]> RenderPngAsync(
        string html,
        CancellationToken cancellationToken = default);
}

public interface IIReGoReportArtifactStore
{
    bool IsConfigured { get; }
    string StorageMode { get; }

    string CreateArtifactRef(
        string patientRef,
        string sessionRef,
        string sourceSnapshot,
        string templateVersion);

    bool TryGet(string artifactRef, out byte[] bytes);
    void Store(string artifactRef, byte[] bytes);
    string IssueAccessToken(string artifactRef, DateTimeOffset expiresAt);
    ReportImageAccessResult Resolve(string token);
    ReportImageAccessResult ResolveArtifact(string artifactRef);
}
