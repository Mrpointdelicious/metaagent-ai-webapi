using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AI_WebApi.Model.DTOs.AITools;
using Newtonsoft.Json.Linq;

namespace AI_WebApi.Services.AITools.IReGo;

/// <summary>
/// Produces deterministic fingerprints from the trusted, normalized report
/// analysis. Only the resolved training scope and report/evaluation content are
/// included; request ids, cache watermarks, URLs and storage state are excluded.
/// </summary>
internal static class IReGoReportContentFingerprint
{
    internal const string SingleContentProfile =
        "irego-single-content-v1";
    internal const string SingleEvaluationRules =
        "irego-single-evaluation-v2";
    internal const string LongitudinalContentProfile =
        "irego-longitudinal-content-v1";
    internal const string LongitudinalEvaluationRules =
        "irego-longitudinal-evaluation-v1";

    public static string SingleTrainingScope(
        IReGoSessionAnalysisDto analysis) =>
        "irego_single_scope_v1_" + Hash(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("scope_schema", "irego-single-scope-v1");
            writer.WriteString("source_namespace", analysis.Session.SourceSystem);
            writer.WriteString("session_ref", analysis.Session.SessionRef);
            writer.WriteString("plan_ref", analysis.Plan.PlanRef);
            writer.WriteEndObject();
        });

    public static string LongitudinalTrainingScope(
        IReGoLongitudinalAnalysisDto analysis) =>
        "irego_window_scope_v1_" + Hash(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("scope_schema", "irego-longitudinal-scope-v1");
            writer.WriteString("source_namespace", "robotdb_main");
            writer.WritePropertyName("positions");
            writer.WriteStartArray();
            foreach (var report in analysis.Reports
                         .OrderBy(x => x.Ordinal)
                         .ThenBy(x => x.SessionRef, StringComparer.Ordinal))
            {
                writer.WriteStartObject();
                writer.WriteNumber("ordinal", report.Ordinal);
                writer.WriteString("session_ref", report.SessionRef);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        });

    public static string SingleSourceSnapshot(
        IReGoSessionAnalysisDto analysis,
        IReGoSingleSessionEvaluationResult evaluation) =>
        string.Join(
            ":",
            SingleContentProfile,
            SingleEvaluationRules,
            Hash(writer => WriteSingle(writer, analysis, evaluation)));

    public static string LongitudinalSourceSnapshot(
        IReGoLongitudinalAnalysisDto analysis) =>
        string.Join(
            ":",
            LongitudinalContentProfile,
            LongitudinalEvaluationRules,
            Hash(writer => WriteLongitudinal(writer, analysis)));

    private static void WriteSingle(
        Utf8JsonWriter writer,
        IReGoSessionAnalysisDto analysis,
        IReGoSingleSessionEvaluationResult evaluation)
    {
        writer.WriteStartObject();
        writer.WriteString("content_schema", SingleContentProfile);
        writer.WriteString("evaluation_rules", SingleEvaluationRules);
        writer.WriteString("locale", "zh-CN");

        writer.WritePropertyName("time");
        writer.WriteStartObject();
        WriteDate(writer, "scheduled_at", analysis.Time.ScheduledAt);
        WriteDate(
            writer,
            "source_record_created_at",
            analysis.Time.SourceRecordCreatedAt);
        WriteDate(
            writer,
            "execution_started_at",
            analysis.Time.ExecutionStartedAt);
        WriteDate(writer, "report_recorded_at", analysis.Time.ReportRecordedAt);
        WriteDate(writer, "completed_at", analysis.Time.CompletedAt);
        writer.WriteString("training_state", analysis.Time.TrainingState);
        writer.WriteString("state_confidence", analysis.Time.StateConfidence);
        writer.WriteString("time_zone", analysis.Time.TimeZone);
        writer.WriteEndObject();

        writer.WritePropertyName("schema");
        writer.WriteStartObject();
        writer.WriteString("plan_family", analysis.Schema.PlanSchemaFamily);
        writer.WriteString("report_family", analysis.Schema.ReportSchemaFamily);
        writer.WriteString("report_signature", analysis.Schema.ReportSchemaSignature);
        writer.WriteString("validation_status", analysis.Schema.ValidationStatus);
        writer.WriteEndObject();

        writer.WritePropertyName("overview");
        writer.WriteStartObject();
        writer.WriteNumber("report_block_count", analysis.ReportBlocks.Count);
        writer.WriteNumber("valid_metric_count", analysis.Overview.ValidMetricCount);
        writer.WriteNumber(
            "excluded_metric_count",
            analysis.Overview.ExcludedMetricCount);
        WriteDecimal(
            writer,
            "total_training_duration",
            analysis.Overview.TotalTrainingDuration);
        WriteString(writer, "duration_unit", analysis.Overview.DurationUnit);
        WriteString(
            writer,
            "duration_display",
            analysis.Overview.TotalTrainingDurationDisplay);
        WriteSortedStrings(writer, "content_types", analysis.Overview.ContentTypes);
        writer.WriteEndObject();

        writer.WritePropertyName("plan");
        writer.WriteStartObject();
        writer.WriteString("state", analysis.Plan.State);
        writer.WriteString("completion_flag", analysis.Plan.CompletionFlag);
        WriteBlocks(writer, "blocks", analysis.Plan.Blocks);
        writer.WriteEndObject();

        WriteBlocks(writer, "report_blocks", analysis.ReportBlocks);
        WriteBlockComparisons(writer, analysis.BlockComparison);
        WriteSingleEvaluation(writer, evaluation);
        writer.WriteEndObject();
    }

    private static void WriteLongitudinal(
        Utf8JsonWriter writer,
        IReGoLongitudinalAnalysisDto analysis)
    {
        writer.WriteStartObject();
        writer.WriteString("content_schema", LongitudinalContentProfile);
        writer.WriteString("evaluation_rules", LongitudinalEvaluationRules);
        writer.WriteString("locale", "zh-CN");

        writer.WritePropertyName("window");
        writer.WriteStartObject();
        writer.WriteNumber(
            "resolved_report_count",
            analysis.Window.ResolvedReportCount);
        writer.WriteNumber(
            "complete_report_count",
            analysis.Window.CompleteReportCount);
        writer.WriteNumber(
            "incomplete_report_count",
            analysis.Window.IncompleteReportCount);
        writer.WriteBoolean("is_contiguous", analysis.Window.IsContiguous);
        WriteDate(writer, "started_at", analysis.Window.StartedAt);
        WriteDate(writer, "ended_at", analysis.Window.EndedAt);
        writer.WriteString("comparison_mode", analysis.Window.ComparisonMode);
        writer.WriteString(
            "missing_point_policy",
            analysis.Window.MissingPointPolicy);
        writer.WriteEndObject();

        writer.WritePropertyName("reports");
        writer.WriteStartArray();
        foreach (var report in analysis.Reports.OrderBy(x => x.Ordinal))
        {
            writer.WriteStartObject();
            writer.WriteNumber("ordinal", report.Ordinal);
            WriteDate(writer, "session_time", report.SessionTime);
            writer.WriteString("training_state", report.TrainingState);
            writer.WriteString("report_status", report.ReportStatus);
            writer.WriteBoolean(
                "included_in_metric_comparison",
                report.IncludedInMetricComparison);
            writer.WriteNumber("report_block_count", report.ReportBlockCount);
            WriteDecimal(writer, "completion_rate", report.CompletionRate);
            writer.WriteNumber(
                "completion_numerator",
                report.CompletionNumerator);
            writer.WriteNumber(
                "completion_denominator",
                report.CompletionDenominator);
            writer.WriteString("completion_level", report.CompletionLevel);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();

        writer.WritePropertyName("projects");
        writer.WriteStartArray();
        foreach (var project in analysis.Projects)
        {
            WriteProject(writer, project);
        }
        writer.WriteEndArray();

        WriteCapabilities(
            writer,
            "capability_evaluations",
            analysis.CapabilityEvaluations);
        writer.WriteEndObject();
    }

    private static void WriteBlocks(
        Utf8JsonWriter writer,
        string propertyName,
        IReadOnlyList<IReGoContentBlockDto> blocks)
    {
        writer.WritePropertyName(propertyName);
        writer.WriteStartArray();
        foreach (var block in blocks.OrderBy(x => x.Sequence))
        {
            writer.WriteStartObject();
            writer.WriteNumber("sequence", block.Sequence);
            WriteInteger(writer, "report_mode", block.ReportMode);
            WriteInteger(writer, "balance_mode", block.BalanceMode);
            writer.WriteString("rehab_content", block.RehabContent);
            writer.WriteString("interaction_type", block.InteractionType);
            WriteString(writer, "name", block.Name);
            writer.WriteString("display_name", block.DisplayName);
            WriteString(writer, "training_mode_name", block.TrainingModeName);
            WriteMetrics(writer, "metrics", block.Metrics);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }

    private static void WriteMetrics(
        Utf8JsonWriter writer,
        string propertyName,
        IReadOnlyList<MetricFactDto> metrics)
    {
        writer.WritePropertyName(propertyName);
        writer.WriteStartArray();
        for (var index = 0; index < metrics.Count; index++)
        {
            var metric = metrics[index];
            writer.WriteStartObject();
            writer.WriteNumber("ordinal", index + 1);
            writer.WriteString("metric_code", metric.MetricCode);
            writer.WritePropertyName("raw_value");
            WriteCanonicalValue(writer, metric.RawValue);
            WriteDecimal(writer, "normalized_value", metric.NormalizedValue);
            writer.WriteString("value_status", metric.ValueStatus);
            WriteString(writer, "unit", metric.Unit);
            writer.WriteString("unit_status", metric.UnitStatus);
            writer.WriteString("data_stage", metric.DataStage);
            writer.WriteString("display_name", metric.DisplayName);
            writer.WriteString("source_kind", metric.SourceKind);
            writer.WriteString(
                "metric_evidence_level",
                metric.MetricEvidenceLevel);
            writer.WriteString("evaluation_status", metric.EvaluationStatus);
            writer.WritePropertyName("repair");
            if (metric.Repair == null)
            {
                writer.WriteNullValue();
            }
            else
            {
                writer.WriteStartObject();
                writer.WriteString("rule", metric.Repair.Rule);
                writer.WriteString("rule_version", metric.Repair.RuleVersion);
                writer.WriteString("confidence", metric.Repair.Confidence);
                WriteSortedStrings(
                    writer,
                    "source_fields",
                    metric.Repair.SourceFields);
                writer.WriteEndObject();
            }
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }

    private static void WriteBlockComparisons(
        Utf8JsonWriter writer,
        IReadOnlyList<IReGoBlockComparisonDto> comparisons)
    {
        writer.WritePropertyName("block_comparison");
        writer.WriteStartArray();
        foreach (var comparison in comparisons
                     .OrderBy(x => x.PlanSequence ?? int.MaxValue)
                     .ThenBy(x => x.ReportSequence ?? int.MaxValue))
        {
            writer.WriteStartObject();
            WriteInteger(writer, "plan_sequence", comparison.PlanSequence);
            WriteInteger(writer, "report_sequence", comparison.ReportSequence);
            WriteString(writer, "plan_display_name", comparison.PlanDisplayName);
            WriteString(
                writer,
                "report_display_name",
                comparison.ReportDisplayName);
            writer.WriteString("status", comparison.Status);
            WriteSortedStrings(writer, "matched_signals", comparison.MatchedSignals);
            WriteSortedStrings(
                writer,
                "missing_from_report",
                comparison.MissingFromReport);
            WriteSortedStrings(
                writer,
                "unplanned_in_report",
                comparison.UnplannedInReport);
            WriteSortedStrings(writer, "warnings", comparison.Warnings);
            writer.WritePropertyName("metric_comparisons");
            writer.WriteStartArray();
            foreach (var metric in comparison.MetricComparisons
                         .OrderBy(x => x.PlannedMetricCode, StringComparer.Ordinal)
                         .ThenBy(x => x.ReportedMetricCode, StringComparer.Ordinal))
            {
                writer.WriteStartObject();
                writer.WriteString(
                    "planned_metric_code",
                    metric.PlannedMetricCode);
                WriteString(
                    writer,
                    "reported_metric_code",
                    metric.ReportedMetricCode);
                WriteDecimal(writer, "planned_value", metric.PlannedValue);
                WriteDecimal(writer, "reported_value", metric.ReportedValue);
                WriteString(writer, "unit", metric.Unit);
                writer.WriteBoolean("comparable", metric.Comparable);
                writer.WriteString("relation", metric.Relation);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }

    private static void WriteSingleEvaluation(
        Utf8JsonWriter writer,
        IReGoSingleSessionEvaluationResult evaluation)
    {
        writer.WritePropertyName("evaluation");
        writer.WriteStartObject();
        WriteDecimal(writer, "completion_rate", evaluation.CompletionRate);
        writer.WriteNumber(
            "completion_numerator",
            evaluation.CompletionNumerator);
        writer.WriteNumber(
            "completion_denominator",
            evaluation.CompletionDenominator);
        writer.WriteString("completion_level", evaluation.CompletionLevel);
        writer.WriteString(
            "completion_level_label",
            evaluation.CompletionLevelLabel);
        writer.WritePropertyName("games");
        writer.WriteStartArray();
        foreach (var game in evaluation.Games.OrderBy(x => x.ReportSequence))
        {
            writer.WriteStartObject();
            writer.WriteNumber("report_sequence", game.ReportSequence);
            writer.WriteString("display_name", game.DisplayName);
            writer.WriteString("score", game.Score);
            writer.WriteString("performance_level", game.PerformanceLevel);
            writer.WriteString(
                "performance_level_label",
                game.PerformanceLevelLabel);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteProject(
        Utf8JsonWriter writer,
        IReGoLongitudinalProjectDto project)
    {
        writer.WriteStartObject();
        writer.WriteString("project_key", project.ProjectKey);
        writer.WriteString("display_name", project.DisplayName);
        writer.WriteString("rehab_content", project.RehabContent);
        writer.WriteString("interaction_type", project.InteractionType);
        WriteInteger(writer, "game_mode", project.GameMode);
        WriteInteger(writer, "balance_mode", project.BalanceMode);
        writer.WriteNumber(
            "same_project_lane_count",
            project.SameProjectLaneCount);
        writer.WritePropertyName("lanes");
        writer.WriteStartArray();
        foreach (var lane in project.Lanes.OrderBy(x => x.SameProjectOrdinal))
        {
            writer.WriteStartObject();
            writer.WriteNumber(
                "same_project_ordinal",
                lane.SameProjectOrdinal);
            writer.WriteString("display_name", lane.DisplayName);
            writer.WritePropertyName("segments");
            writer.WriteStartArray();
            foreach (var segment in lane.Segments.OrderBy(x => x.ReportOrdinal))
            {
                writer.WriteStartObject();
                writer.WriteNumber("report_ordinal", segment.ReportOrdinal);
                writer.WriteString("status", segment.Status);
                WriteInteger(writer, "report_sequence", segment.ReportSequence);
                WriteString(writer, "display_name", segment.DisplayName);
                WriteString(
                    writer,
                    "configuration_signature",
                    segment.ConfigurationSignature);
                WriteInteger(writer, "game_difficulty", segment.GameDifficulty);
                WriteMetrics(writer, "metrics", segment.Metrics);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WritePropertyName("metric_series");
            writer.WriteStartArray();
            foreach (var series in lane.MetricSeries)
            {
                WriteMetricSeries(writer, series);
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        WriteCapabilities(
            writer,
            "capability_evaluations",
            project.CapabilityEvaluations);
        writer.WriteEndObject();
    }

    private static void WriteMetricSeries(
        Utf8JsonWriter writer,
        IReGoLongitudinalMetricSeriesDto series)
    {
        writer.WriteStartObject();
        writer.WriteString("metric_code", series.MetricCode);
        writer.WriteString("display_name", series.DisplayName);
        writer.WriteString("schema_family", series.SchemaFamily);
        WriteString(writer, "unit", series.Unit);
        writer.WriteString("unit_status", series.UnitStatus);
        writer.WriteBoolean("comparison_eligible", series.ComparisonEligible);
        writer.WriteBoolean("chart_eligible", series.ChartEligible);
        writer.WriteNumber(
            "longest_same_condition_run",
            series.LongestSameConditionRun);
        WriteSortedStrings(
            writer,
            "comparison_limitations",
            series.ComparisonLimitations);
        writer.WritePropertyName("points");
        writer.WriteStartArray();
        foreach (var point in series.Points.OrderBy(x => x.ReportOrdinal))
        {
            writer.WriteStartObject();
            writer.WriteNumber("report_ordinal", point.ReportOrdinal);
            writer.WriteString("status", point.Status);
            writer.WritePropertyName("raw_value");
            WriteCanonicalValue(writer, point.RawValue);
            WriteDecimal(writer, "normalized_value", point.NormalizedValue);
            WriteString(writer, "display_value", point.DisplayValue);
            WriteInteger(writer, "game_difficulty", point.GameDifficulty);
            WriteString(
                writer,
                "configuration_signature",
                point.ConfigurationSignature);
            writer.WriteString("line_from_previous", point.LineFromPrevious);
            writer.WriteString("comparison_scope", point.ComparisonScope);
            WriteSortedStrings(
                writer,
                "comparison_limitations",
                point.ComparisonLimitations);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        WriteInterpretation(writer, series.Interpretation);
        writer.WriteEndObject();
    }

    private static void WriteInterpretation(
        Utf8JsonWriter writer,
        IReGoLongitudinalInterpretationDto interpretation)
    {
        writer.WritePropertyName("interpretation");
        writer.WriteStartObject();
        writer.WriteString("metric_code", interpretation.MetricCode);
        writer.WriteString("mode", interpretation.Mode);
        writer.WriteString("direction", interpretation.Direction);
        writer.WriteString("text", interpretation.Text);
        writer.WriteString("verdict", interpretation.Verdict);
        writer.WriteString("capability_code", interpretation.CapabilityCode);
        writer.WriteString("capability_name", interpretation.CapabilityName);
        writer.WriteString("strength", interpretation.Strength);
        WriteDecimal(writer, "delta", interpretation.Delta);
        writer.WriteString("basic_evaluation", interpretation.BasicEvaluation);
        writer.WriteString(
            "extended_explanation",
            interpretation.ExtendedExplanation);
        writer.WriteString("life_hint", interpretation.LifeHint);
        writer.WriteString("patient_message", interpretation.PatientMessage);
        writer.WriteString(
            "message_rule_version",
            interpretation.MessageRuleVersion);
        writer.WriteString("rule_set_version", interpretation.RuleSetVersion);
        writer.WriteString(
            "explanation_boundary",
            interpretation.ExplanationBoundary);
        writer.WritePropertyName("threshold");
        WriteThreshold(writer, interpretation.Threshold);
        writer.WriteEndObject();
    }

    private static void WriteThreshold(
        Utf8JsonWriter writer,
        IReGoMetricThresholdDto? threshold)
    {
        if (threshold == null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStartObject();
        WriteDecimal(writer, "value", threshold.Value);
        writer.WriteString("unit", threshold.Unit);
        writer.WriteString("source", threshold.Source);
        writer.WriteString("evidence_level", threshold.EvidenceLevel);
        writer.WriteBoolean("is_irego_mdc", threshold.IsIReGoMdc);
        writer.WriteBoolean("production_usable", threshold.ProductionUsable);
        WriteDecimal(
            writer,
            "database_exploratory_value",
            threshold.DatabaseExploratoryValue);
        WriteDecimal(
            writer,
            "external_lower_bound",
            threshold.ExternalLowerBound);
        WriteDecimal(
            writer,
            "observable_change_floor",
            threshold.ObservableChangeFloor);
        WriteDecimal(
            writer,
            "relative_difference_from_external",
            threshold.RelativeDifferenceFromExternal);
        writer.WriteString("selection_policy", threshold.SelectionPolicy);
        WriteString(writer, "evidence_reference", threshold.EvidenceReference);
        writer.WriteString("registry_version", threshold.RegistryVersion);
        writer.WriteEndObject();
    }

    private static void WriteCapabilities(
        Utf8JsonWriter writer,
        string propertyName,
        IReadOnlyList<IReGoCapabilityEvaluationDto> evaluations)
    {
        writer.WritePropertyName(propertyName);
        writer.WriteStartArray();
        foreach (var evaluation in evaluations
                     .OrderBy(x => x.CapabilityCode, StringComparer.Ordinal))
        {
            writer.WriteStartObject();
            writer.WriteString("capability_code", evaluation.CapabilityCode);
            writer.WriteString("capability_name", evaluation.CapabilityName);
            writer.WriteString("verdict", evaluation.Verdict);
            writer.WriteString("evaluation_axis", evaluation.EvaluationAxis);
            writer.WriteString("strength", evaluation.Strength);
            writer.WriteString("basic_evaluation", evaluation.BasicEvaluation);
            writer.WriteString(
                "extended_explanation",
                evaluation.ExtendedExplanation);
            writer.WriteString("life_hint", evaluation.LifeHint);
            writer.WriteString("patient_message", evaluation.PatientMessage);
            WriteSortedStrings(
                writer,
                "evidence_metric_codes",
                evaluation.EvidenceMetricCodes);
            writer.WriteString(
                "message_rule_version",
                evaluation.MessageRuleVersion);
            writer.WriteString("rule_set_version", evaluation.RuleSetVersion);
            writer.WriteString(
                "explanation_boundary",
                evaluation.ExplanationBoundary);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }

    private static string Hash(Action<Utf8JsonWriter> write)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            write(writer);
        }

        return Convert.ToHexString(SHA256.HashData(stream.ToArray()))
            .ToLowerInvariant();
    }

    private static void WriteDate(
        Utf8JsonWriter writer,
        string propertyName,
        DateTimeOffset? value)
    {
        if (!value.HasValue)
        {
            writer.WriteNull(propertyName);
            return;
        }

        writer.WriteString(
            propertyName,
            value.Value.ToString("O", CultureInfo.InvariantCulture));
    }

    private static void WriteDecimal(
        Utf8JsonWriter writer,
        string propertyName,
        decimal? value)
    {
        if (!value.HasValue)
        {
            writer.WriteNull(propertyName);
            return;
        }

        writer.WriteString(propertyName, CanonicalDecimal(value.Value));
    }

    private static void WriteInteger(
        Utf8JsonWriter writer,
        string propertyName,
        int? value)
    {
        if (value.HasValue)
        {
            writer.WriteNumber(propertyName, value.Value);
        }
        else
        {
            writer.WriteNull(propertyName);
        }
    }

    private static void WriteString(
        Utf8JsonWriter writer,
        string propertyName,
        string? value)
    {
        if (value == null)
        {
            writer.WriteNull(propertyName);
        }
        else
        {
            writer.WriteString(propertyName, Normalize(value));
        }
    }

    private static void WriteSortedStrings(
        Utf8JsonWriter writer,
        string propertyName,
        IEnumerable<string> values)
    {
        writer.WritePropertyName(propertyName);
        writer.WriteStartArray();
        foreach (var value in values
                     .Select(Normalize)
                     .OrderBy(x => x, StringComparer.Ordinal))
        {
            writer.WriteStringValue(value);
        }
        writer.WriteEndArray();
    }

    private static void WriteCanonicalValue(
        Utf8JsonWriter writer,
        object? value)
    {
        writer.WriteStartObject();
        switch (value)
        {
            case null:
                writer.WriteString("kind", "null");
                writer.WriteNull("value");
                break;
            case JToken token:
                writer.WriteString("kind", "json");
                writer.WritePropertyName("value");
                WriteToken(writer, token);
                break;
            case bool boolean:
                writer.WriteString("kind", "boolean");
                writer.WriteBoolean("value", boolean);
                break;
            case byte or sbyte or short or ushort or int or uint or long or
                ulong or decimal:
                writer.WriteString("kind", "number");
                writer.WriteString(
                    "value",
                    CanonicalDecimal(Convert.ToDecimal(
                        value,
                        CultureInfo.InvariantCulture)));
                break;
            case double doubleValue when double.IsFinite(doubleValue):
                writer.WriteString("kind", "number");
                writer.WriteString(
                    "value",
                    doubleValue.ToString("R", CultureInfo.InvariantCulture));
                break;
            case float floatValue when float.IsFinite(floatValue):
                writer.WriteString("kind", "number");
                writer.WriteString(
                    "value",
                    floatValue.ToString("R", CultureInfo.InvariantCulture));
                break;
            case DateTimeOffset dateTimeOffset:
                writer.WriteString("kind", "datetime_offset");
                writer.WriteString(
                    "value",
                    dateTimeOffset.ToString("O", CultureInfo.InvariantCulture));
                break;
            case DateTime dateTime:
                writer.WriteString("kind", "datetime");
                writer.WriteString(
                    "value",
                    dateTime.ToString("O", CultureInfo.InvariantCulture));
                break;
            default:
                writer.WriteString("kind", "string");
                writer.WriteString("value", Normalize(
                    value.ToString() ?? string.Empty));
                break;
        }
        writer.WriteEndObject();
    }

    private static void WriteToken(Utf8JsonWriter writer, JToken token)
    {
        switch (token)
        {
            case JObject jsonObject:
                writer.WriteStartObject();
                foreach (var property in jsonObject.Properties()
                             .OrderBy(x => x.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(Normalize(property.Name));
                    WriteToken(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JArray array:
                writer.WriteStartArray();
                foreach (var item in array)
                {
                    WriteToken(writer, item);
                }
                writer.WriteEndArray();
                break;
            case JValue jsonValue:
                WriteCanonicalValue(writer, jsonValue.Value);
                break;
            default:
                writer.WriteStringValue(Normalize(token.ToString()));
                break;
        }
    }

    private static string CanonicalDecimal(decimal value) =>
        value.ToString(
            "0.############################",
            CultureInfo.InvariantCulture);

    private static string Normalize(string value) =>
        value.Normalize(NormalizationForm.FormC);
}
