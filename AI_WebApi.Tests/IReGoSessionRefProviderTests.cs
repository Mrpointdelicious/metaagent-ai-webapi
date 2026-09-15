using System.Text.RegularExpressions;
using AI_WebApi.Model.AITools;
using AI_WebApi.Services.AITools.IReGo;
using Microsoft.Extensions.Options;

namespace AI_WebApi.Tests;

public sealed class IReGoSessionRefProviderTests
{
    private const string TestSigningKey =
        "unit-test-only-persistent-signing-key-32-bytes";

    [Fact]
    public void Same_session_is_stable_across_provider_recreation()
    {
        var firstProvider = Provider();
        var first = firstProvider.Create(3, 461, 1001, 2002, "rf_schema");
        var repeated = firstProvider.Create(3, 461, 1001, 2002, "rf_schema");
        var afterRecreation = Provider().Create(
            3,
            461,
            1001,
            2002,
            "rf_schema");

        Assert.Equal(first, repeated);
        Assert.Equal(first, afterRecreation);
    }

    [Fact]
    public void Different_patient_or_report_produces_different_reference()
    {
        var provider = Provider();
        var baseline = provider.Create(3, 461, 1001, 2002, "rf_schema");

        Assert.NotEqual(
            baseline,
            provider.Create(3, 462, 1001, 2002, "rf_schema"));
        Assert.NotEqual(
            baseline,
            provider.Create(3, 461, 1001, 2003, "rf_schema"));
    }

    [Fact]
    public void Reference_contains_only_versioned_opaque_signature()
    {
        var provider = Provider();
        var sessionRef = provider.Create(
            987654,
            876543,
            765432,
            654321,
            "rf_sensitive_schema");

        Assert.Matches(
            new Regex("^irego_s_v2_[0-9a-f]{32}$", RegexOptions.CultureInvariant),
            sessionRef);
        Assert.DoesNotContain("patient", sessionRef, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("plan", sessionRef, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("report", sessionRef, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rf_sensitive_schema", sessionRef, StringComparison.Ordinal);
    }

    [Fact]
    public void Match_is_bound_to_tenant_patient_plan_report_and_schema()
    {
        var provider = Provider();
        var sessionRef = provider.Create(3, 461, 1001, 2002, "rf_schema");

        Assert.True(
            provider.Matches(
                sessionRef,
                3,
                461,
                1001,
                2002,
                "rf_schema"));
        Assert.False(
            provider.Matches(
                sessionRef,
                3,
                462,
                1001,
                2002,
                "rf_schema"));
        Assert.False(
            provider.Matches(
                "irego_s_v2_not-a-signature",
                3,
                461,
                1001,
                2002,
                "rf_schema"));
    }

    [Fact]
    public void Signing_key_shorter_than_32_bytes_is_rejected()
    {
        var provider = new IReGoSessionRefProvider(
            Options.Create(new IReGoToolOptions
            {
                SessionRefSigningKey = "too-short"
            }));

        Assert.False(provider.IsConfigured);
        Assert.Throws<InvalidOperationException>(() =>
            provider.Create(3, 461, 1001, 2002, "rf_schema"));
    }

    [Fact]
    public void Plan_reference_is_stable_and_opaque()
    {
        var first = Provider().CreatePlan(3, 461, 1001);
        var repeated = Provider().CreatePlan(3, 461, 1001);

        Assert.Equal(first, repeated);
        Assert.Matches(
            new Regex("^irego_p_v2_[0-9a-f]{32}$", RegexOptions.CultureInvariant),
            first);
        Assert.DoesNotContain("461", first, StringComparison.Ordinal);
        Assert.DoesNotContain("1001", first, StringComparison.Ordinal);
    }

    internal static IReGoSessionRefProvider Provider() =>
        new(Options.Create(new IReGoToolOptions
        {
            SessionRefSigningKey = TestSigningKey
        }));
}
