using AI_WebApi.IServices.AITools;
using AI_WebApi.Model.AITools;
using AI_WebApi.Model.Domain.IReGo;
using AI_WebApi.Model.DTOs.AITools;
using AI_WebApi.Model.Entities;
using AI_WebApi.Services.AITools.IReGo;
using Microsoft.Extensions.Options;

namespace AI_WebApi.Tests;

public sealed class IReGoPatientHistoryServiceTests
{
    [Fact]
    public async Task History_is_pageable_and_returns_only_stable_anonymous_refs()
    {
        var service = Service(Evaluation());
        var response = await service.GetAsync(
            Context(),
            new GetIReGoPatientHistoryRequestDto
            {
                PageNumber = 1,
                PageSize = 1
            },
            "request-1");

        Assert.Equal(ToolStatuses.Success, response.Status);
        Assert.Equal(2, response.Data!.Page.TotalItems);
        Assert.Equal(2, response.Data.Page.TotalPages);
        Assert.True(response.Data.Page.HasNext);
        var item = Assert.Single(response.Data.Items);
        Assert.StartsWith("irego_p_v1_", item.PlanRef);
        Assert.StartsWith("irego_s_v1_", item.SessionRef);
        Assert.True(item.EvaluationAvailability.SingleSession);
        Assert.DoesNotContain("461", item.PlanRef);
    }

    [Fact]
    public async Task Future_plan_is_discoverable_without_session_ref()
    {
        var response = await Service(Evaluation()).GetAsync(
            Context(),
            new GetIReGoPatientHistoryRequestDto
            {
                PageNumber = 1,
                PageSize = 10,
                TrainingState = "not_started"
            },
            "request-1");

        var item = Assert.Single(response.Data!.Items);
        Assert.Equal("plan", item.RecordType);
        Assert.Null(item.SessionRef);
        Assert.Equal("该计划尚未开始。", item.PatientMessage);
    }

    private static IReGoPatientHistoryService Service(
        IReGoPatientEvaluation evaluation) => new(
            new FakeEvaluationService(evaluation),
            IReGoSessionRefProviderTests.Provider(),
            new IReGoStrokeCapabilityMapper(),
            Options.Create(new IReGoToolOptions
            {
                SessionRefSigningKey =
                    "unit-test-only-persistent-signing-key-32-bytes"
            }));

    private static TrustedPatientContext Context() => new()
    {
        PatientId = 461,
        SubjectRef = "dify_user:test"
    };

    private static IReGoPatientEvaluation Evaluation()
    {
        var parsed = new IReGoReportParser().Parse(
            """
            {
              "ReportList": [
                {
                  "ReportMode": 1,
                  "WalkMode": 1,
                  "WTrainingWalktime": 100,
                  "WTrainingWalkdistance": 50,
                  "WAvgSpeed": 0
                }
              ],
              "ReportProcess": "1"
            }
            """);
        var completedPlan = Plan(
            1001,
            new DateTime(2026, 7, 20, 10, 0, 0),
            1);
        var futurePlan = Plan(
            1002,
            new DateTime(2026, 7, 19, 10, 0, 0),
            0);
        var report = new RehabReportEntity
        {
            Id = 2001,
            PlanId = completedPlan.Id,
            CreateTime = completedPlan.BookingTime,
            ReportDetails = "{}"
        };
        var session = new IReGoEvaluatedSession
        {
            Plan = completedPlan,
            Report = report,
            ParsedReport = parsed,
            State = new TrainingStateResult
            {
                State = "completed",
                Confidence = "high"
            },
            Usable = true,
            ValidationStatus = "structurally_valid"
        };
        return new IReGoPatientEvaluation
        {
            Raw = new IReGoRawDataSet
            {
                Patient = new PatientEntity
                {
                    Id = 461,
                    BusinessId = 3
                },
                SourceCompleteness = "authoritative_primary"
            },
            Sessions = new[] { session },
            Plans = new[]
            {
                new IReGoEvaluatedPlan
                {
                    Plan = completedPlan,
                    State = session.State,
                    ParsedPlan = new ParsedIReGoPlan(),
                    EffectiveSession = session
                },
                new IReGoEvaluatedPlan
                {
                    Plan = futurePlan,
                    State = new TrainingStateResult
                    {
                        State = "not_started",
                        Confidence = "medium"
                    },
                    ParsedPlan = new ParsedIReGoPlan()
                }
            },
            Watermark = "test-watermark"
        };
    }

    private static RehabPlanEntity Plan(
        int id,
        DateTime bookingTime,
        int isComplete) => new()
        {
            Id = id,
            UserId = 461,
            BusinessId = 3,
            BookingTime = bookingTime,
            CreateTime = bookingTime.AddDays(-1),
            IsComplete = isComplete
        };

    private sealed class FakeEvaluationService : IIReGoPatientEvaluationService
    {
        private readonly IReGoPatientEvaluation _evaluation;

        public FakeEvaluationService(IReGoPatientEvaluation evaluation)
        {
            _evaluation = evaluation;
        }

        public Task<IReGoPatientEvaluation> EvaluateAsync(
            TrustedPatientContext context,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_evaluation);
    }
}
