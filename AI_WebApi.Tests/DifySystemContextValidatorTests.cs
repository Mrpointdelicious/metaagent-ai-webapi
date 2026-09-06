using AI_WebApi.Model.AITools;
using AI_WebApi.Model.DTOs.AITools;

namespace AI_WebApi.Tests;

public sealed class DifySystemContextValidatorTests
{
    [Fact]
    public void Valid_service_token_and_system_context_are_accepted()
    {
        var result = Validator().Validate(
            "Bearer tool-secret",
            SystemContext(),
            "trace-1");

        Assert.True(result.IsSuccess);
        Assert.Equal(7, result.Context!.PatientId);
        Assert.Equal("dify_user:7", result.Context.SubjectRef);
        Assert.Equal("conversation-42", result.Context.ConversationId);
    }

    [Fact]
    public void System_context_without_service_token_is_rejected()
    {
        var result = Validator().Validate(
            null,
            SystemContext(),
            "trace-1");

        Assert.False(result.IsSuccess);
        Assert.Equal(
            IReGoReasonCodes.DifyServiceAuthenticationRequired,
            result.ReasonCode);
    }

    [Fact]
    public void Wrong_service_token_is_rejected()
    {
        var result = Validator().Validate(
            "Bearer wrong-secret",
            SystemContext(),
            "trace-1");

        Assert.False(result.IsSuccess);
        Assert.Equal(
            IReGoReasonCodes.DifyServiceAuthenticationFailed,
            result.ReasonCode);
    }

    [Fact]
    public void Missing_system_context_is_rejected_after_service_authentication()
    {
        var result = Validator().Validate(
            "Bearer tool-secret",
            null,
            "trace-1");

        Assert.False(result.IsSuccess);
        Assert.Equal(
            IReGoReasonCodes.DifySystemContextMissing,
            result.ReasonCode);
    }

    [Fact]
    public void Non_numeric_dify_user_is_rejected()
    {
        var result = Validator().Validate(
            "Bearer tool-secret",
            new DifySystemContextDto
            {
                User = "end-user-9f2"
            },
            "trace-1");

        Assert.False(result.IsSuccess);
        Assert.Equal(
            IReGoReasonCodes.DifyUserIdInvalid,
            result.ReasonCode);
    }

    private static DifySystemContextValidator Validator() =>
        new(new IReGoToolOptions
        {
            DifyServiceBearerToken = "tool-secret"
        });

    private static DifySystemContextDto SystemContext() => new()
    {
        User = "7",
        ConversationId = "conversation-42"
    };
}
