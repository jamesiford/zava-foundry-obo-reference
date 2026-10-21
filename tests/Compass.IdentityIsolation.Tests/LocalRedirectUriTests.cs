using System.Text.RegularExpressions;

namespace Compass.IdentityIsolation.Tests;

/// <summary>
/// The localhost SPA redirect URI is a developer convenience and must not be registered on a deployed environment:
/// the script registers it only when asked, and azd asks only for the dev environment.
/// </summary>
public sealed partial class LocalRedirectUriTests
{
    private static readonly string Infra = LocateInfra();

    [Fact]
    public void SetupEntraApps_RegistersNoLocalhostUri_UnlessLocalPortIsPassed()
    {
        var script = File.ReadAllText(Path.Combine(Infra, "scripts", "Setup-EntraApps.ps1"));

        Assert.Matches(EmptyLocalPortDefault(), script);
        Assert.DoesNotContain("@(5240)", script, StringComparison.Ordinal);
        Assert.Contains("if ($LocalPort.Count -gt 0)", script, StringComparison.Ordinal);
    }

    [Fact]
    public void AzdHooks_PassLocalPort_ForTheDevEnvironmentOnly()
    {
        var yaml = File.ReadAllText(Path.Combine(Infra, "azure.yaml"));

        var passes = LocalPortAssignment().Matches(yaml);
        Assert.Equal(2, passes.Count);
        Assert.All(passes, m => Assert.Contains("$env:AZURE_ENV_NAME -eq 'dev'", m.Value, StringComparison.Ordinal));
        Assert.Equal(2, Regex.Matches(yaml, @"Setup-EntraApps\.ps1 @entra").Count);
    }

    private static string LocateInfra()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "infra", "azure.yaml");
            if (File.Exists(candidate))
            {
                return Path.GetDirectoryName(candidate)!;
            }
        }

        throw new DirectoryNotFoundException("Could not locate infra/azure.yaml above the test output.");
    }

    [GeneratedRegex(@"\[int\[\]\]\$LocalPort\s*=\s*@\(\s*\)")]
    private static partial Regex EmptyLocalPortDefault();

    [GeneratedRegex(@"if \(\$env:AZURE_ENV_NAME -eq '[^']*'\) \{ \$entra\.LocalPort = 5240 \}")]
    private static partial Regex LocalPortAssignment();
}
