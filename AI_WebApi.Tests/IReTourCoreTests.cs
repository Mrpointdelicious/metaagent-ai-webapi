using AI_WebApi.IServices.AITools;
using AI_WebApi.Model.AITools;
using AI_WebApi.Model.Domain.IReGo;
using AI_WebApi.Model.Domain.IReTour;
using AI_WebApi.Model.DTOs.AITools;
using AI_WebApi.Model.Entities;
using AI_WebApi.Repository.AITools;
using AI_WebApi.Services.AITools.IReTour;
using Microsoft.Extensions.Options;

namespace AI_WebApi.Tests;

/// <summary>
/// IReTour core logic tests with synthetic fixtures only; no patient PII and
/// no production payloads (design doc section 13).
/// </summary>
public sealed class IReTourCoreTests
{
    private static IReTourToolOptions ToolOptions(string key = "synthetic-signing-key") =>
        new() { SessionRefSigningKey = key };

    private static IOptions<IReTourToolOptions> Wrapped(string key = "synthetic-signing-key") =>
        Options.Create(ToolOptions(key));

    private static IReTourWalkEntity Walk(
        int id,
        string? start,
        string? end,
        int itemId = 0,
        int deviceId = 1,
        int userId = 100,
        int? duration = 60) => new()
        {
            Id = id,
            StartTime = start,
            EndTime = end,
            ParseTime = null,
            CreateTime = null,
            DeviceId = deviceId,
            ItemId = itemId,
            UserId = userId,
            Duration = duration
        };

    private static IReTourSummaryReportEntity Summary(
        int id,
        int planId,
        int itemId,
        string details) => new()
        {
            Id = id,
            PlanId = planId,
            ItemId = itemId,
            Details = details
        };

    // ---- Time semantics ----

    [Fact]
    public void Time_resolver_parses_merged_format_and_sentinel()
    {
        var resolver = new IReTourTimeSemanticResolver();

        var parsed = resolver.Resolve(Walk(1, "2024/06/02 12:10:17", "2024/06/02 12:12:18"));
        Assert.NotNull(parsed.ExecutionStartedAt);
        Assert.Equal(8, parsed.ExecutionStartedAt!.Value.Offset.Hours);
        Assert.True(parsed.ExecutionEndedAt > parsed.ExecutionStartedAt);
        Assert.Empty(parsed.Issues);

        var sentinel = resolver.Resolve(Walk(2, "0001/01/01 00:00:00", "0001/01/01 00:00:00"));
        Assert.Null(sentinel.ExecutionStartedAt);
        Assert.Null(sentinel.ExecutionEndedAt);

        var broken = resolver.Resolve(Walk(3, "not-a-time", null));
        Assert.Contains(broken.Issues, x => x.Code == IReTourReasonCodes.TimeInvalid);
    }

    [Fact]
    public void Time_resolver_flags_start_after_end()
    {
        var resolver = new IReTourTimeSemanticResolver();

        var parsed = resolver.Resolve(Walk(1, "2024/06/02 12:20:00", "2024/06/02 12:10:00"));
        Assert.Contains(parsed.Issues, x => x.Code == IReTourReasonCodes.TimeOrderInvalid);
    }

    // ---- Training state ----

    [Fact]
    public void State_resolver_follows_evidence_order()
    {
        var resolver = new IReTourTrainingStateResolver();
        var timeResolver = new IReTourTimeSemanticResolver();

        var completed = resolver.Resolve(timeResolver.Resolve(
            Walk(1, "2024/06/02 12:10:00", "2024/06/02 12:12:00")));
        Assert.Equal(IReTourExecutionStates.Completed, completed.State);

        var interrupted = resolver.Resolve(timeResolver.Resolve(
            Walk(2, "2024/06/02 12:10:00", null)));
        Assert.Equal(IReTourExecutionStates.Interrupted, interrupted.State);

        var unknown = resolver.Resolve(timeResolver.Resolve(
            Walk(3, "0001/01/01 00:00:00", "0001/01/01 00:00:00")));
        Assert.Equal(IReTourExecutionStates.ExecutionUnknown, unknown.State);
    }

    // ---- Item registry ----

    [Theory]
    [InlineData(0, "straight_primary")]
    [InlineData(2, "reaction_primary")]
    [InlineData(4, "balance_bridge")]
    [InlineData(8, "sideway")]
    [InlineData(9, "gait_assessment")]
    public void Item_registry_resolves_known_items(int itemId, string canonicalCode)
    {
        var item = IReTourItemRegistry.Resolve(itemId);
        Assert.NotNull(item);
        Assert.Equal(canonicalCode, item!.CanonicalCode);
    }

    [Fact]
    public void Item_registry_fails_closed_on_unknown_item()
    {
        Assert.Null(IReTourItemRegistry.Resolve(10));
        Assert.Null(IReTourItemRegistry.Resolve(-1));
    }

    // ---- Summary extraction (whitelist + numeric hygiene) ----

    [Fact]
    public void Reaction_summary_extracts_whitelisted_metrics_only()
    {
        var parser = IReTourSummaryParserRegistry.Resolve(2)!;
        const string details =
            "{\"name\":\"synthetic\",\"phone\":\"13800000000\",\"correctRate\":0.92," +
            "\"errorRate\":0.08,\"completionRate\":1.0,\"correctSum\":12,\"errorSum\":1," +
            "\"avg_ReactionSpeed\":1.2,\"max_ReactionSpeed\":2.1,\"total_Lights\":16," +
            "\"rice_ItemInfo\":{\"x\":1},\"raw_array\":[1,2,3]}";

        var parsed = IReTourSummaryExtraction.Parse(details, parser);

        Assert.Contains(parsed.Metrics, x => x.MetricCode == "reaction_correct_rate" && x.RawValue == 0.92m);
        Assert.Contains(parsed.Metrics, x => x.MetricCode == "reaction_total_lights" && x.RawValue == 16m);
        Assert.DoesNotContain(parsed.Metrics, x => x.SourceField is "name" or "phone" or "rice_ItemInfo" or "raw_array");
    }

    [Fact]
    public void Non_finite_and_out_of_range_values_are_excluded_not_dropped()
    {
        var parser = IReTourSummaryParserRegistry.Resolve(2)!;
        const string details =
            "{\"correctRate\":\"NaN\",\"errorRate\":1.5,\"completionRate\":0.5}";

        var parsed = IReTourSummaryExtraction.Parse(details, parser);

        var nonFinite = parsed.Metrics.Single(x => x.MetricCode == "reaction_correct_rate");
        Assert.Null(nonFinite.NormalizedValue);
        Assert.Equal("invalid", nonFinite.ValueStatus);
        Assert.Contains(parsed.Issues, x => x.Code == IReTourReasonCodes.NonFiniteMetricExcluded);

        var outOfRange = parsed.Metrics.Single(x => x.MetricCode == "reaction_error_rate");
        Assert.Equal("invalid", outOfRange.ValueStatus);
        Assert.Contains(parsed.Issues, x => x.Code == IReTourReasonCodes.MetricOutOfRange);

        Assert.Equal("valid", parsed.Metrics.Single(x => x.MetricCode == "reaction_completion_rate").ValueStatus);
    }

    [Fact]
    public void Broken_summary_json_is_schema_unsupported()
    {
        var parser = IReTourSummaryParserRegistry.Resolve(0)!;
        var parsed = IReTourSummaryExtraction.Parse("{broken", parser);
        Assert.Contains(parsed.Issues, x =>
            x.Code == IReTourReasonCodes.SchemaUnsupported && x.Severity == "error");
    }

    // ---- Report link classification ----

    [Fact]
    public void Report_link_verifies_matching_fk_and_flags_mismatch()
    {
        var resolver = new IReTourReportLinkResolver();
        var walk = Walk(1, "2024/06/02 12:10:00", "2024/06/02 12:12:00", itemId: 2);

        var verified = resolver.Resolve(
            walk,
            new[] { Summary(10, 1, 2, "{\"correctRate\":0.5}") },
            Array.Empty<IReTourRawReportEntity>(),
            Array.Empty<IReTourCognitiveReportEntity>());
        Assert.True(verified.SummaryVerified);
        Assert.False(verified.SummaryConflict);

        var mismatch = resolver.Resolve(
            walk,
            new[] { Summary(10, 1, 3, "{\"correctRate\":0.5}") },
            Array.Empty<IReTourRawReportEntity>(),
            Array.Empty<IReTourCognitiveReportEntity>());
        Assert.False(mismatch.SummaryVerified);
        Assert.True(mismatch.SummaryConflict);
    }

    [Fact]
    public void Duplicate_reports_with_different_content_conflict()
    {
        var resolver = new IReTourReportLinkResolver();
        var walk = Walk(1, null, null, itemId: 0);

        var link = resolver.Resolve(
            walk,
            new[]
            {
                Summary(10, 1, 0, "{\"avg_Speed\":1.0}"),
                Summary(11, 1, 0, "{\"avg_Speed\":2.0}")
            },
            Array.Empty<IReTourRawReportEntity>(),
            Array.Empty<IReTourCognitiveReportEntity>());
        Assert.True(link.SummaryConflict);
    }

    // ---- Session references ----

    private static IReTourEvaluatedSession Session(int walkId, int patientId = 100)
    {
        var time = new IReTourTimeSemanticResolver().Resolve(
            Walk(walkId, "2024/06/02 12:10:00", "2024/06/02 12:12:00"));
        return new IReTourEvaluatedSession
        {
            CanonicalSessionKey = $"walk:{walkId}",
            WalkId = walkId,
            PatientId = patientId,
            BusinessId = 1,
            PatientBindingNamespace = "dbuser",
            PatientBindingVersion = 1,
            ItemId = 0,
            CanonicalCode = "straight_primary",
            SchemaFamily = "straight_summary_v1",
            DisplayName = "直行训练",
            DeviceVerified = true,
            Time = time,
            ExecutionState = IReTourExecutionStates.Completed,
            ResultState = IReTourResultStates.SummaryAvailable,
            Metrics = new[]
            {
                new IReTourMetricFact { MetricCode = "straight_speed", RawValue = 1.0m, ValueStatus = "valid" }
            }
        };
    }

    [Fact]
    public void Session_ref_round_trips_for_same_patient()
    {
        var provider = new IReTourSessionRefProvider(Wrapped());
        var session = Session(42);

        var issued = provider.Issue(session);
        Assert.StartsWith("iretour_s_v1_", issued);

        var verified = provider.Verify(issued, 100, new[] { session });
        Assert.NotNull(verified);
        Assert.Equal("walk:42", verified!.CanonicalSessionKey);
    }

    [Fact]
    public void Session_ref_rejects_other_patient_and_tampering()
    {
        var provider = new IReTourSessionRefProvider(Wrapped());
        var session = Session(42);

        var issued = provider.Issue(session);
        Assert.Null(provider.Verify(issued, 999, new[] { session }));
        Assert.Null(provider.Verify(issued + "x", 100, new[] { session }));
        Assert.Null(provider.Verify("irego_s_v2_whatever", 100, new[] { session }));
    }

    // ---- Evaluation gate (device / item) ----

    private sealed class FakeIReTourRepository : IIReTourDataRepository
    {
        private readonly IReTourRawDataSet _data;

        public FakeIReTourRepository(IReTourRawDataSet data)
        {
            _data = data;
        }

        public Task<IReTourRawDataSet> LoadPatientDataAsync(
            TrustedPatientContext context,
            CancellationToken cancellationToken) =>
            Task.FromResult(_data);
    }

    private static IReTourRawDataSet DataSet(
        int deviceType,
        int walkItemId,
        int reportItemId) => new()
        {
            Patient = new IReTourPatientEntity { Id = 100, BusinessId = 1 },
            Binding = new IReTourPatientBinding { Verified = true, BindingVersion = 1 },
            SourceUserIds = new[] { 100 },
            Walks = new[]
            {
                Walk(1, "2024/06/02 12:10:00", "2024/06/02 12:12:00", itemId: walkItemId)
            },
            Devices = new Dictionary<int, IReTourDeviceEntity>
            {
                [1] = new() { Id = 1, DeviceType = deviceType }
            },
            SummaryReports = new[] { Summary(10, 1, reportItemId, "{\"avg_Speed\":1.0}") }
        };

    private static async Task<IReTourEvaluationResult> EvaluateAsync(IReTourRawDataSet data)
    {
        var service = new IReTourPatientEvaluationService(
            new FakeIReTourRepository(data),
            new IReTourTimeSemanticResolver(),
            new IReTourTrainingStateResolver(),
            new IReTourReportLinkResolver());
        return await service.EvaluateAsync(
            new TrustedPatientContext { PatientId = 100 },
            "request-1",
            CancellationToken.None);
    }

    [Fact]
    public async Task Device_outside_iretour_family_is_blocked()
    {
        var result = await EvaluateAsync(DataSet(deviceType: 1, walkItemId: 0, reportItemId: 0));

        var session = Assert.Single(result.Sessions);
        Assert.False(session.Visible);
        Assert.Contains(session.Issues, x => x.Code == IReTourReasonCodes.DeviceReferenceInvalid);
    }

    [Fact]
    public async Task Unknown_item_fails_closed()
    {
        var result = await EvaluateAsync(DataSet(deviceType: 6, walkItemId: 42, reportItemId: 42));

        var session = Assert.Single(result.Sessions);
        Assert.False(session.Visible);
        Assert.Contains(session.Issues, x => x.Code == IReTourReasonCodes.ItemUnknown);
    }

    [Fact]
    public async Task Healthy_session_passes_gate_with_conflict_report_link()
    {
        var result = await EvaluateAsync(DataSet(deviceType: 6, walkItemId: 2, reportItemId: 3));

        var session = Assert.Single(result.Sessions);
        Assert.True(session.Visible);
        Assert.Equal(IReTourResultStates.ResultConflicting, session.ResultState);
        Assert.Contains(session.Issues, x => x.Code == IReTourReasonCodes.ReportLinkConflict);
    }
}
