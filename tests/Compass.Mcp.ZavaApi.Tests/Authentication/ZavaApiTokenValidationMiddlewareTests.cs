using Compass.Mcp.ZavaApi.Host.Authentication;
using Compass.Mcp.ZavaApi.Tests.Support;
using Compass.Shared.Identity.Tokens;
using Microsoft.Extensions.Logging.Abstractions;

namespace Compass.Mcp.ZavaApi.Tests.Authentication;

/// <summary>
/// Functional tests of the hop-6 gate: the real middleware and real JWT validator, driven with signed tokens, with
/// only the Functions host (context) and Entra (key source) replaced.
/// </summary>
public sealed class ZavaApiTokenValidationMiddlewareTests : IDisposable
{
    private readonly TestTokenIssuer _issuer = new();

    public void Dispose() => _issuer.Dispose();

    [Fact]
    public async Task Valid_token_reaches_the_tool()
    {
        var (called, outcome) = await InvokeAsync(WithToken(_issuer.CreateToken()));

        Assert.Null(outcome);
        Assert.True(called);
    }

    [Fact]
    public async Task Token_header_name_is_matched_case_insensitively()
    {
        var headers = new Dictionary<string, string> { ["X-Client-Zava-Api-Token"] = _issuer.CreateToken() };

        var (called, outcome) = await InvokeAsync(headers);

        Assert.Null(outcome);
        Assert.True(called);
    }

    [Fact]
    public async Task Expired_token_is_rejected()
    {
        var (called, outcome) = await InvokeAsync(WithToken(_issuer.CreateExpiredToken()));

        AssertRejected(called, outcome, TokenValidationStatus.Expired);
    }

    [Fact]
    public async Task Token_for_another_audience_is_rejected()
    {
        var (called, outcome) = await InvokeAsync(WithToken(_issuer.CreateToken(audience: "api://some-other-api")));

        AssertRejected(called, outcome, TokenValidationStatus.InvalidAudience);
    }

    [Fact]
    public async Task Token_from_an_untrusted_issuer_is_rejected()
    {
        var (called, outcome) = await InvokeAsync(
            WithToken(_issuer.CreateToken(issuer: "https://sts.example.com/22222222-2222-2222-2222-222222222222/")));

        AssertRejected(called, outcome, TokenValidationStatus.InvalidIssuer);
    }

    [Fact]
    public async Task Missing_token_header_is_rejected()
    {
        var (called, outcome) = await InvokeAsync(new Dictionary<string, string> { ["x-ms-user-identity"] = "someone" });

        AssertRejected(called, outcome, TokenValidationStatus.Missing);
    }

    [Fact]
    public async Task Blank_token_is_rejected()
    {
        var (called, outcome) = await InvokeAsync(WithToken("  "));

        AssertRejected(called, outcome, TokenValidationStatus.Missing);
    }

    [Fact]
    public async Task Request_without_any_headers_is_rejected()
    {
        var (called, outcome) = await InvokeAsync(headers: null);

        AssertRejected(called, outcome, TokenValidationStatus.Missing);
    }

    [Fact]
    public async Task Request_without_an_invocation_context_is_rejected()
    {
        var (called, outcome) = await InvokeAsync(WithToken(_issuer.CreateToken()), publishInvocationContext: false);

        AssertRejected(called, outcome, TokenValidationStatus.Missing);
    }

    [Fact]
    public async Task Token_signed_with_an_untrusted_key_is_rejected()
    {
        var forged = _issuer.CreateToken(signingKey: TestTokenIssuer.CreateUntrustedKey());

        var (called, outcome) = await InvokeAsync(WithToken(forged));

        AssertRejected(called, outcome, TokenValidationStatus.InvalidSignature);
    }

    [Fact]
    public async Task Garbage_token_is_rejected()
    {
        var (called, outcome) = await InvokeAsync(WithToken("not-a-jwt"));

        AssertRejected(called, outcome, TokenValidationStatus.Malformed);
    }

    [Fact]
    public async Task Rejection_message_never_contains_the_token()
    {
        var token = _issuer.CreateToken(audience: "api://some-other-api");

        var (_, outcome) = await InvokeAsync(WithToken(token));

        Assert.DoesNotContain(token, outcome!.Message, StringComparison.Ordinal);
    }

    private static Dictionary<string, string> WithToken(string token) =>
        new() { [CompassHeaders.ZavaApiToken] = token };

    private static void AssertRejected(bool called, ZavaApiUnauthorizedException? outcome, TokenValidationStatus status)
    {
        Assert.False(called, "The tool must not run when the token is rejected.");
        Assert.NotNull(outcome);
        Assert.Contains(status.ToString(), outcome.Message, StringComparison.Ordinal);
    }

    private async Task<(bool NextCalled, ZavaApiUnauthorizedException? Rejection)> InvokeAsync(
        IReadOnlyDictionary<string, string>? headers,
        bool publishInvocationContext = true)
    {
        var middleware = new ZavaApiTokenValidationMiddleware(
            _issuer.CreateValidator(),
            NullLogger<ZavaApiTokenValidationMiddleware>.Instance);
        var context = new FakeFunctionContext(headers, publishInvocationContext);
        var called = false;

        try
        {
            await middleware.Invoke(context, _ =>
            {
                called = true;
                return Task.CompletedTask;
            });
            return (called, null);
        }
        catch (ZavaApiUnauthorizedException ex)
        {
            return (called, ex);
        }
    }
}
