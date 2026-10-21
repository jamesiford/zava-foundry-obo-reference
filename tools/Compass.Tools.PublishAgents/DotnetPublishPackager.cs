using System.Diagnostics;

namespace Compass.Tools.PublishAgents;

/// <summary>Runs <c>dotnet publish</c> for linux-x64, framework-dependent, so the zip matches the <c>dotnet_10</c> runtime.</summary>
public sealed class DotnetPublishPackager : IAgentPackager
{
    public async Task<string> PackageAsync(HostedAgentSpec spec, string repositoryRoot, CancellationToken cancellationToken)
    {
        var output = Path.Combine(Path.GetTempPath(), "compass-publish", spec.Name, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(output);

        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = repositoryRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in new[]
                 {
                     "publish", spec.ProjectPath, "-c", "Release", "-r", "linux-x64", "--self-contained", "false",
                     "-o", output, "-nologo", "-v", "quiet",
                 })
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start 'dotnet publish'.");
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"dotnet publish failed for {spec.Name} (exit {process.ExitCode}):{Environment.NewLine}{await stdout}{await stderr}");
        }

        if (!File.Exists(Path.Combine(output, spec.AssemblyName + ".dll")))
        {
            throw new InvalidOperationException($"dotnet publish produced no {spec.AssemblyName}.dll at the root of {output}; the platform entry point would not start.");
        }

        return output;
    }
}
