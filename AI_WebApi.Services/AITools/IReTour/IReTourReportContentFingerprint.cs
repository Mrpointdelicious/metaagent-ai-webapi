using System.Security.Cryptography;
using System.Text.Json;
using AI_WebApi.Model.AITools;
using AI_WebApi.Model.DTOs.AITools;

namespace AI_WebApi.Services.AITools.IReTour;

internal static class IReTourReportContentFingerprint
{
    public static string SingleTrainingScope(IReTourSessionAnalysisDto analysis) =>
        Hash(new
        {
            kind = "iretour_single_scope_v1",
            analysis.Session.SessionRef,
            analysis.Activity.CanonicalCode
        });

    public static string SingleSourceSnapshot(IReTourSessionAnalysisDto analysis) =>
        Hash(new
        {
            kind = "iretour_single_snapshot_v1",
            analysis.Session.SessionRef,
            analysis.Time.ExecutionStartedAt,
            analysis.Time.ExecutionEndedAt,
            analysis.Time.SourceRecordCreatedAt,
            analysis.Time.TrainingState,
            analysis.Activity.CanonicalCode,
            analysis.Activity.DisplayName,
            analysis.Activity.RegistryVersion,
            duration_seconds = analysis.Plan.DurationSeconds,
            analysis.Plan.SemanticsStatus,
            analysis.Plan.DurationSourceField,
            analysis.Plan.ParametersSource,
            parameters = analysis.Plan.Parameters.Select(x => new
            {
                x.Code,
                x.DisplayName,
                x.Value
            }),
            analysis.Result.ResultState,
            analysis.Interpretation,
            analysis.Quality,
            analysis.Sources,
            metrics = analysis.Result.Metrics.Select(x => new
            {
                x.MetricCode,
                x.DisplayName,
                x.NormalizedValue,
                x.Unit,
                x.ValueStatus,
                x.RegistryVersion,
                x.SourceField,
                x.UnitStatus
            })
        });

    public static string LongitudinalTrainingScope(
        IReTourLongitudinalAnalysisDto analysis) =>
        Hash(new
        {
            kind = "iretour_longitudinal_scope_v1",
            activity = analysis.Sessions.FirstOrDefault()?.CanonicalCode,
            sessions = analysis.Sessions.Select(x => x.SessionRef)
        });

    public static string LongitudinalSourceSnapshot(
        IReTourLongitudinalAnalysisDto analysis) =>
        Hash(new
        {
            kind = "iretour_longitudinal_snapshot_v1",
            item_registry = IReTourContract.ItemRegistryVersion,
            analysis.Window.StartedAt,
            analysis.Window.EndedAt,
            analysis.Window.ResolvedCount,
            analysis.Window.ExcludedSessionCount,
            analysis.Interpretation,
            analysis.Issues,
            sessions = analysis.Sessions.Select(x => new
            {
                x.Ordinal,
                x.SessionRef,
                x.SessionTime,
                x.CanonicalCode,
                x.TrainingState,
                x.ResultState,
                x.ExclusionReasonCodes
            }),
            series = analysis.MetricSeries.Select(x => new
            {
                x.MetricCode,
                x.DisplayName,
                x.Unit,
                x.Direction,
                x.RegistryVersion,
                x.SourceField,
                x.ComparisonKey,
                x.ComparisonStatus,
                points = x.Points.Select(point => new
                {
                    point.Ordinal,
                    point.Value,
                    point.ValueStatus
                })
            })
        });

    private static string Hash<T>(T value)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(value);
        return Convert.ToHexString(SHA256.HashData(json)).ToLowerInvariant();
    }
}
