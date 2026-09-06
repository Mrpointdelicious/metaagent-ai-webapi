using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using AI_WebApi.Model.AITools;
using AI_WebApi.Model.DTOs.AITools;

namespace AI_WebApi.Services.AITools.IReGo;

internal static class IReGoSingleSessionReportEvaluation
{
    private static readonly IReadOnlyDictionary<string, string>
        CompletionLevelLabels =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["low"] = "低",
                ["boundary"] = "分界值",
                ["high"] = "高"
            };

    private static readonly IReadOnlyDictionary<string, string>
        GamePerformanceLevelLabels =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["general"] = "一般",
                ["good"] = "良好",
                ["excellent"] = "优秀"
            };

    private static readonly IReadOnlySet<string> CompletionEligibleContents =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "walking",
            "sit_stand",
            "game",
            "balance_training",
            "balance_assessment",
            "single_walking",
            "single_sit"
        };

    public static IReGoSingleSessionEvaluationResult Validate(
        IReGoSessionAnalysisDto analysis,
        IReGoSingleSessionAiEvaluationDto? input)
    {
        if (input == null)
        {
            return Invalid(IReGoReasonCodes.SingleReportAiEvaluationRequired);
        }

        var facts = analysis.ReportEvaluationFacts.CompletionRate.HasValue
            ? analysis.ReportEvaluationFacts
            : CreateFacts(
                analysis.Plan.Blocks,
                analysis.BlockComparison,
                analysis.ReportBlocks);
        if (!facts.CompletionRate.HasValue)
        {
            return Invalid(IReGoReasonCodes.SingleReportAiEvaluationMismatch);
        }

        var completionLevel = Normalize(input.CompletionLevel);
        var expectedCompletionLevel = facts.CompletionLevel;
        if (completionLevel != expectedCompletionLevel)
        {
            return Invalid(IReGoReasonCodes.SingleReportAiEvaluationMismatch);
        }

        var games = facts.Games.OrderBy(x => x.ReportSequence).ToArray();
        var suppliedGames = input.GameEvaluations ??
            Array.Empty<IReGoGamePerformanceEvaluationDto>();

        if (suppliedGames.Count != games.Length ||
            suppliedGames.Select(x => x.ReportSequence).Distinct().Count() !=
            suppliedGames.Count)
        {
            return Invalid(IReGoReasonCodes.SingleReportAiEvaluationMismatch);
        }

        var gameResults = new List<IReGoSingleSessionGameEvaluationResult>();
        foreach (var game in games)
        {
            var supplied = suppliedGames.SingleOrDefault(x =>
                x.ReportSequence == game.ReportSequence);
            if (supplied == null)
            {
                return Invalid(IReGoReasonCodes.SingleReportAiEvaluationMismatch);
            }

            var level = Normalize(supplied.PerformanceLevel);
            if (!GamePerformanceLevelLabels.TryGetValue(level, out var label))
            {
                return Invalid(IReGoReasonCodes.SingleReportAiEvaluationInvalid);
            }

            gameResults.Add(new IReGoSingleSessionGameEvaluationResult
            {
                ReportSequence = game.ReportSequence,
                DisplayName = game.DisplayName,
                Score = game.Score?.ToString(
                    "0.###",
                    CultureInfo.InvariantCulture) ?? string.Empty,
                PerformanceLevel = level,
                PerformanceLevelLabel = label
            });
        }

        var canonical = string.Join(
            "|",
            "irego-single-evaluation-v1",
            completionLevel,
            string.Join(
                ",",
                gameResults.Select(x =>
                    $"{x.ReportSequence}:{x.PerformanceLevel}")));
        var fingerprint = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(canonical))
                    .AsSpan(0, 16))
            .ToLowerInvariant();

        return new IReGoSingleSessionEvaluationResult
        {
            IsValid = true,
            CompletionRate = facts.CompletionRate.Value,
            CompletionNumerator = facts.CompletionNumerator,
            CompletionDenominator = facts.CompletionDenominator,
            CompletionLevel = completionLevel,
            CompletionLevelLabel = CompletionLevelLabels[completionLevel],
            Games = gameResults,
            Fingerprint = fingerprint
        };
    }

    public static IReGoSingleSessionEvaluationResult CreateBackend(
        IReGoSessionAnalysisDto analysis)
    {
        var facts = analysis.ReportEvaluationFacts.CompletionRate.HasValue
            ? analysis.ReportEvaluationFacts
            : CreateFacts(
                analysis.Plan.Blocks,
                analysis.BlockComparison,
                analysis.ReportBlocks);
        if (!facts.CompletionRate.HasValue ||
            !CompletionLevelLabels.TryGetValue(
                facts.CompletionLevel,
                out var completionLabel))
        {
            return Invalid(IReGoReasonCodes.SingleReportAiEvaluationMismatch);
        }

        var games = facts.Games
            .OrderBy(x => x.ReportSequence)
            .Select(x => new IReGoSingleSessionGameEvaluationResult
            {
                ReportSequence = x.ReportSequence,
                DisplayName = x.DisplayName,
                Score = x.Score?.ToString(
                    "0.###",
                    CultureInfo.InvariantCulture) ?? string.Empty,
                PerformanceLevel = "not_evaluated",
                PerformanceLevelLabel = "未评价"
            })
            .ToArray();
        var presentation = new IReGoSingleSessionAiEvaluationDto
        {
            CompletionLevel = facts.CompletionLevel,
            GameEvaluations = games.Select(x =>
                new IReGoGamePerformanceEvaluationDto
                {
                    ReportSequence = x.ReportSequence,
                    PerformanceLevel = x.PerformanceLevel
                }).ToArray()
        };
        var canonical = string.Join(
            "|",
            "irego-single-backend-evaluation-v2",
            facts.CompletionLevel,
            string.Join(",", games.Select(x => x.ReportSequence)));
        return new IReGoSingleSessionEvaluationResult
        {
            IsValid = true,
            CompletionRate = facts.CompletionRate.Value,
            CompletionNumerator = facts.CompletionNumerator,
            CompletionDenominator = facts.CompletionDenominator,
            CompletionLevel = facts.CompletionLevel,
            CompletionLevelLabel = completionLabel,
            Games = games,
            Fingerprint = Convert.ToHexString(
                    SHA256.HashData(Encoding.UTF8.GetBytes(canonical))
                        .AsSpan(0, 16))
                .ToLowerInvariant(),
            Presentation = presentation
        };
    }

    public static IReGoSingleSessionEvaluationFactsDto CreateFacts(
        IReadOnlyList<IReGoContentBlockDto> planBlocks,
        IReadOnlyList<IReGoBlockComparisonDto> comparisons,
        IReadOnlyList<IReGoContentBlockDto> reportBlocks)
    {
        var eligibleSequences = planBlocks
            .Where(block =>
                CompletionEligibleContents.Contains(block.RehabContent))
            .Select(block => block.Sequence)
            .Distinct()
            .ToArray();
        if (eligibleSequences.Length == 0)
        {
            return new IReGoSingleSessionEvaluationFactsDto();
        }

        var eligibleSet = eligibleSequences.ToHashSet();
        var numerator = comparisons
            .Where(comparison =>
                comparison.PlanSequence.HasValue &&
                comparison.ReportSequence.HasValue &&
                eligibleSet.Contains(comparison.PlanSequence.Value) &&
                comparison.Status is "matched_exact" or "matched_with_warning")
            .Select(comparison => comparison.PlanSequence!.Value)
            .Distinct()
            .Count();
        var rate = decimal.Round(
            numerator * 100m / eligibleSequences.Length,
            1,
            MidpointRounding.AwayFromZero);
        var completionLevel = rate switch
        {
            < 70m => "low",
            > 70m => "high",
            _ => "boundary"
        };
        var games = reportBlocks
            .Where(IsGame)
            .Select(block =>
            {
                var scoreMetric = block.Metrics.FirstOrDefault(metric =>
                    metric.MetricCode == "game_score" &&
                    IsVisible(metric));
                return new
                {
                    Block = block,
                    Score = Score(scoreMetric)
                };
            })
            .Where(x => x.Score.HasValue)
            .OrderBy(x => x.Block.Sequence)
            .Select(x => new IReGoGameEvaluationFactDto
            {
                ReportSequence = x.Block.Sequence,
                DisplayName = DisplayName(x.Block),
                Score = x.Score
            })
            .ToArray();

        return new IReGoSingleSessionEvaluationFactsDto
        {
            CompletionRate = rate,
            CompletionLevel = completionLevel,
            CompletionNumerator = numerator,
            CompletionDenominator = eligibleSequences.Length,
            Games = games
        };
    }

    private static bool IsGame(IReGoContentBlockDto block) =>
        block.InteractionType is "game" or "vr_game" ||
        block.Metrics.Any(metric => metric.MetricCode == "game_score");

    private static bool IsVisible(MetricFactDto metric) =>
        metric.ValueStatus == "valid" &&
        metric.RawValue != null &&
        !string.IsNullOrWhiteSpace(FormatValue(metric));

    private static string DisplayName(IReGoContentBlockDto block) =>
        !string.IsNullOrWhiteSpace(block.DisplayName)
            ? block.DisplayName.Trim()
            : !string.IsNullOrWhiteSpace(block.Name)
                ? block.Name.Trim()
                : "训练游戏";

    private static string FormatValue(MetricFactDto metric) =>
        metric.RawValue switch
        {
            null => string.Empty,
            decimal value => value.ToString("0.###", CultureInfo.InvariantCulture),
            double value when double.IsFinite(value) =>
                value.ToString("0.###", CultureInfo.InvariantCulture),
            float value when float.IsFinite(value) =>
                value.ToString("0.###", CultureInfo.InvariantCulture),
            IFormattable value => value.ToString(null, CultureInfo.InvariantCulture),
            _ => metric.RawValue.ToString() ?? string.Empty
        };

    private static decimal? Score(MetricFactDto? metric)
    {
        if (metric == null)
        {
            return null;
        }

        if (metric.NormalizedValue.HasValue)
        {
            return metric.NormalizedValue.Value;
        }

        return decimal.TryParse(
            metric.RawValue?.ToString(),
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out var score)
                ? score
                : null;
    }

    private static string Normalize(string? value) =>
        value?.Trim().ToLowerInvariant() ?? string.Empty;

    private static IReGoSingleSessionEvaluationResult Invalid(string reason) =>
        new()
        {
            IsValid = false,
            ReasonCode = reason
        };

}

internal sealed class IReGoSingleSessionEvaluationResult
{
    public bool IsValid { get; init; }
    public string? ReasonCode { get; init; }
    public decimal CompletionRate { get; init; }
    public int CompletionNumerator { get; init; }
    public int CompletionDenominator { get; init; }
    public string CompletionLevel { get; init; } = string.Empty;
    public string CompletionLevelLabel { get; init; } = string.Empty;
    public IReadOnlyList<IReGoSingleSessionGameEvaluationResult> Games
        { get; init; } = Array.Empty<IReGoSingleSessionGameEvaluationResult>();
    public string Fingerprint { get; init; } = string.Empty;
    public IReGoSingleSessionAiEvaluationDto Presentation { get; init; } = new();
}

internal sealed class IReGoSingleSessionGameEvaluationResult
{
    public int ReportSequence { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public string Score { get; init; } = string.Empty;
    public string PerformanceLevel { get; init; } = string.Empty;
    public string PerformanceLevelLabel { get; init; } = string.Empty;
}
