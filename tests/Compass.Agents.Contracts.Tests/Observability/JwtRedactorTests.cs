using System.Diagnostics;
using Compass.Agents.Contracts.Tests.Support;
using Compass.Shared.Observability.Redaction;

namespace Compass.Agents.Contracts.Tests.Observability;

public sealed class JwtRedactorTests
{
    [Fact]
    public void Redacts_a_jwt_embedded_in_a_message()
    {
        var token = FakeTokens.Create("embedded");

        var redacted = JwtRedactor.Redact($"Authorization failed for {token} on retry");

        Assert.DoesNotContain(token, redacted);
        Assert.Contains(JwtRedactor.Replacement, redacted);
        Assert.StartsWith("Authorization failed for ", redacted);
    }

    [Theory]
    [InlineData("plain text")]
    [InlineData("https://contoso.sharepoint.com/sites/lending/Shared%20Documents/policy.docx")]
    [InlineData("ZCU-100234")]
    [InlineData("eyJhY2Nlc3NfdG9rZW4iOnt9fQ")]
    public void Leaves_ordinary_text_and_claims_challenges_alone(string text)
    {
        Assert.Equal(text, JwtRedactor.Redact(text));
        Assert.False(JwtRedactor.ContainsToken(text));
    }

    [Fact]
    public void Handles_null_and_empty()
    {
        Assert.Null(JwtRedactor.Redact(null));
        Assert.Equal(string.Empty, JwtRedactor.Redact(string.Empty));
    }

    [Fact]
    public void Activity_processor_scrubs_tags_and_display_name()
    {
        var token = FakeTokens.Create("span");
        using var activity = new Activity($"call {token}");
        activity.SetTag("http.request.header", token);
        activity.SetTag("safe", "value");

        new TokenRedactingActivityProcessor().OnEnd(activity);

        Assert.DoesNotContain(token, activity.DisplayName);
        Assert.Equal(JwtRedactor.Replacement, activity.GetTagItem("http.request.header"));
        Assert.Equal("value", activity.GetTagItem("safe"));
    }
}
