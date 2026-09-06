using AI_WebApi.Model.AITools;
using AI_WebApi.Model.DTOs.AITools;
using AI_WebApi.Repository.AITools;
using AI_WebApi.Services.AITools.IReGo;
using Microsoft.Extensions.Options;

namespace AI_WebApi.Tests;

public sealed class PatientIdentityMappingServiceTests
{
    [Fact]
    public async Task ResolveAsync_ReturnsMapping_WhenExactlyOneCandidateExists()
    {
        var service = CreateService(456);

        var result = await service.ResolveAsync(123, "request-1");

        Assert.Equal(ToolStatuses.Success, result.Status);
        Assert.Empty(result.ReasonCodes);
        Assert.NotNull(result.Data);
        Assert.Equal(123, result.Data!.ProjectPatientId);
        Assert.Equal(456, result.Data.RobotPatientId);
        Assert.Equal("exact_name_and_patient_type", result.Data.MatchMethod);
    }

    [Fact]
    public async Task ResolveAsync_ReturnsBindingNotFound_WhenNoCandidateExists()
    {
        var service = CreateService();

        var result = await service.ResolveAsync(123, "request-2");

        Assert.Equal(ToolStatuses.Failed, result.Status);
        Assert.Null(result.Data);
        Assert.Equal(
            new[] { IReGoReasonCodes.PatientBindingNotFound },
            result.ReasonCodes);
    }

    [Fact]
    public async Task ResolveAsync_ReturnsBindingNotFound_WhenMultipleCandidatesExist()
    {
        var service = CreateService(456, 789);

        var result = await service.ResolveAsync(123, "request-3");

        Assert.Equal(ToolStatuses.Failed, result.Status);
        Assert.Null(result.Data);
        Assert.Equal(
            new[] { IReGoReasonCodes.PatientBindingNotFound },
            result.ReasonCodes);
    }

    [Fact]
    public async Task ResolveByPhoneAsync_ReturnsMapping_WhenExactlyOneCandidateExists()
    {
        var service = CreateServiceWithPhone(
            new PatientIdentityMappingCandidate(123, 456));

        var result = await service.ResolveByPhoneAsync(
            "15932910402",
            "request-4");

        Assert.Equal(ToolStatuses.Success, result.Status);
        Assert.NotNull(result.Data);
        Assert.Equal(123, result.Data!.ProjectPatientId);
        Assert.Equal(456, result.Data.RobotPatientId);
    }

    [Fact]
    public async Task ResolveByPhoneAsync_ReturnsBindingNotFound_WhenNotUnique()
    {
        var service = CreateServiceWithPhone(
            new PatientIdentityMappingCandidate(123, 456),
            new PatientIdentityMappingCandidate(123, 789));

        var result = await service.ResolveByPhoneAsync(
            "15932910402",
            "request-5");

        Assert.Equal(ToolStatuses.Failed, result.Status);
        Assert.Null(result.Data);
        Assert.Equal(
            new[] { IReGoReasonCodes.PatientBindingNotFound },
            result.ReasonCodes);
    }

    private static PatientIdentityMappingService CreateService(
        params int[] candidates) =>
        new(
            new StubRepository(
                candidates,
                Array.Empty<PatientIdentityMappingCandidate>()),
            Options.Create(new IReGoToolOptions()));

    private static PatientIdentityMappingService CreateServiceWithPhone(
        params PatientIdentityMappingCandidate[] candidates) =>
        new(
            new StubRepository(Array.Empty<int>(), candidates),
            Options.Create(new IReGoToolOptions()));

    private sealed class StubRepository : IPatientIdentityMappingRepository
    {
        private readonly IReadOnlyList<int> _candidates;
        private readonly IReadOnlyList<PatientIdentityMappingCandidate>
            _phoneCandidates;

        public StubRepository(
            IReadOnlyList<int> candidates,
            IReadOnlyList<PatientIdentityMappingCandidate> phoneCandidates)
        {
            _candidates = candidates;
            _phoneCandidates = phoneCandidates;
        }

        public Task<IReadOnlyList<int>> FindRobotPatientCandidatesAsync(
            int projectPatientId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_candidates);

        public Task<IReadOnlyList<PatientIdentityMappingCandidate>>
            FindRobotPatientCandidatesByPhoneAsync(
                string phone,
                CancellationToken cancellationToken = default) =>
            Task.FromResult(_phoneCandidates);
    }
}
