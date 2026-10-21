namespace Compass.Bot.Application.Tests;

/// <summary>
/// Contract rule 3 as a structural check: <c>x-ms-user-identity</c> may be set only by the Foundry gateway. A source scan
/// is crude, but it fails the build the moment anyone adds a second place that can assert a person to Foundry.
/// </summary>
public sealed class UserIdentityHeaderUsageTests
{
    [Fact]
    public void Only_the_foundry_gateway_references_the_user_identity_header_in_bot_code()
    {
        var root = FindRepositoryRoot();
        var offenders = Directory.EnumerateFiles(Path.Combine(root, "src", "Bot"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Where(f => File.ReadLines(f).Any(IsCodeReferencingUserIdentity))
            .Select(f => Path.GetFileName(f))
            .ToArray();

        Assert.Equal(["FoundryHostedAgentGateway.cs"], offenders);
    }

    private static bool IsCodeReferencingUserIdentity(string line) =>
        !line.TrimStart().StartsWith("//", StringComparison.Ordinal)
        && (line.Contains("CompassHeaders.UserIdentity", StringComparison.Ordinal) || line.Contains("\"x-ms-user-identity\"", StringComparison.OrdinalIgnoreCase));

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Compass.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Compass.slnx not found above the test output directory.");
    }
}
