using System.Text.RegularExpressions;
using Compass.Bot.Host.FrontDoor;

namespace Compass.IdentityIsolation.Tests;

/// <summary>
/// Static guards on the shipped page: it renders model and agent output, so it must never turn that text into markup
/// or run inline script, and the CSP header must keep forbidding it.
/// </summary>
public sealed partial class FrontDoorPageTests
{
    private static readonly string Wwwroot = LocateWwwroot();

    [Fact]
    public void IndexHtml_HasNoInlineScriptOrHandlers()
    {
        var html = File.ReadAllText(Path.Combine(Wwwroot, "index.html"));

        Assert.DoesNotMatch(InlineScript(), html);
        Assert.DoesNotMatch(InlineHandler(), html);
        Assert.DoesNotContain("style=", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AppJs_NeverWritesHtml()
    {
        var script = File.ReadAllText(Path.Combine(Wwwroot, "app.js"));

        foreach (var sink in new[] { "innerHTML", "outerHTML", "insertAdjacentHTML", "document.write", "eval(", "new Function" })
        {
            Assert.DoesNotContain(sink, script, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void AppJs_HoldsNoSecretOrLongLivedCredential()
    {
        var script = File.ReadAllText(Path.Combine(Wwwroot, "app.js"));

        Assert.DoesNotContain("localStorage", script, StringComparison.Ordinal);
        Assert.DoesNotContain("client_secret", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotMatch(Guid(), script);
    }

    [Fact]
    public void AppJs_RetriesAStepUpDemandCarriedByAnErrorEvent_OnceWithTheDecodedClaims()
    {
        var script = File.ReadAllText(Path.Combine(Wwwroot, "app.js"));

        // A branch-raised demand arrives as `event: error` on a 200 stream, not as a 401 header.
        Assert.Contains("payload.claims", script, StringComparison.Ordinal);
        Assert.Contains("decodeClaims(payload.claims)", script, StringComparison.Ordinal);
        Assert.Contains("attempt < 2", script, StringComparison.Ordinal);
        Assert.Contains("readEvents(response, attempt === 0)", script, StringComparison.Ordinal);
    }

    [Fact]
    public void AppJs_RendersTheAnswerFromTextNodesOnly()
    {
        var script = File.ReadAllText(Path.Combine(Wwwroot, "app.js"));

        // Bullets, headings and bold are built from DOM nodes; the answer text is never parsed as markup.
        Assert.Contains("function renderAnswer(", script, StringComparison.Ordinal);
        Assert.Contains("document.createTextNode(", script, StringComparison.Ordinal);
        Assert.Contains("renderAnswer($('answer'), answer.answer)", script, StringComparison.Ordinal);
        Assert.DoesNotContain("$('answer').textContent = answer.answer", script, StringComparison.Ordinal);
    }

    [Fact]
    public void StaticAssets_AreServedWithSecurityHeadersAndRevalidatedOnEveryLoad()
    {
        var response = new Microsoft.AspNetCore.Http.DefaultHttpContext().Response;
        var apply = typeof(FrontDoorExtensions).GetMethod("ApplySecurityHeaders", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

        apply.Invoke(null, [response]);

        Assert.Equal(FrontDoorExtensions.ContentSecurityPolicy, response.Headers.ContentSecurityPolicy.ToString());
        Assert.Equal("no-cache", response.Headers.CacheControl.ToString());
        Assert.Equal("no-referrer", response.Headers["Referrer-Policy"].ToString());
    }

    [Fact]
    public void ContentSecurityPolicy_ForbidsInlineScriptAndFraming()
    {
        var csp = FrontDoorExtensions.ContentSecurityPolicy;

        Assert.Contains("script-src 'self'", csp, StringComparison.Ordinal);
        Assert.DoesNotContain("unsafe-inline", csp, StringComparison.Ordinal);
        Assert.DoesNotContain("unsafe-eval", csp, StringComparison.Ordinal);
        Assert.Contains("frame-ancestors 'none'", csp, StringComparison.Ordinal);
        Assert.Contains("default-src 'none'", csp, StringComparison.Ordinal);
    }

    [Fact]
    public void VendoredMsal_MatchesThePinnedHash()
    {
        var bytes = File.ReadAllBytes(Path.Combine(Wwwroot, "lib", "msal-browser", "msal-browser.min.js"));

        Assert.Equal("2B580165064A6A4EC041EBD44832299172688A23A42C3B81D65314799DBA04EB", Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)));
    }

    private static string LocateWwwroot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "src", "Bot", "Compass.Bot.Host", "wwwroot");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException("Could not locate src/Bot/Compass.Bot.Host/wwwroot above the test output.");
    }

    [GeneratedRegex(@"<script(?![^>]*\bsrc=)", RegexOptions.IgnoreCase)]
    private static partial Regex InlineScript();

    [GeneratedRegex(@"\son(click|load|error|submit|change|input)\s*=", RegexOptions.IgnoreCase)]
    private static partial Regex InlineHandler();

    [GeneratedRegex(@"[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}", RegexOptions.IgnoreCase)]
    private static partial Regex Guid();
}
