using Compass.Agents.Contracts.Errors;

namespace Compass.Agents.Contracts.Tests.Contracts;

public sealed class AgentErrorsWireTests
{
    [Fact]
    public void Claims_round_trip_through_the_packed_message()
    {
        var original = new AgentError(AgentErrorCodes.ClaimsChallenge, "Graph needs more authentication.", "eyJhY2Nlc3NfdG9rZW4iOnt9fQ==");

        var restored = AgentErrors.Unpack(original.Code, AgentErrors.PackMessage(original));

        Assert.Equal(original, restored);
    }

    [Fact]
    public void An_error_without_claims_is_left_untouched()
    {
        var error = new AgentError(AgentErrorCodes.MissingToken, "header is missing");

        Assert.Equal(error.Message, AgentErrors.PackMessage(error));
        Assert.Equal(error, AgentErrors.Unpack(error.Code, error.Message));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("plain text [not claims]")]
    public void Unpack_tolerates_messages_it_did_not_produce(string? message)
    {
        var error = AgentErrors.Unpack("server_error", message);

        Assert.Null(error.Claims);
        Assert.Equal(message ?? string.Empty, error.Message);
    }
}
