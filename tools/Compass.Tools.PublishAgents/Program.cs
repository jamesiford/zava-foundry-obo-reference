using Azure.Identity;
using Compass.Tools.PublishAgents;

var parsed = CommandLine.Parse(args, Environment.GetEnvironmentVariable, Directory.GetCurrentDirectory());
if (parsed.ShowHelp)
{
    Console.WriteLine(CommandLine.Usage);
    return 0;
}

if (parsed.Settings is not { } settings)
{
    Console.Error.WriteLine(parsed.Error);
    Console.Error.WriteLine();
    Console.Error.WriteLine(CommandLine.Usage);
    return 2;
}

try
{
    var specs = HostedAgentCatalog.Create(settings);

    if (settings.WhatIf)
    {
        Console.Write(PublishPlanFormatter.Format(specs, settings));
        return 0;
    }

    using var cancellation = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        cancellation.Cancel();
    };

    var publisher = new AgentPublisher(
        new DotnetPublishPackager(),
        new FoundryAgentVersionService(new Uri(settings.ProjectEndpoint!), new DefaultAzureCredential()),
        TimeProvider.System,
        pollInterval: TimeSpan.FromSeconds(10),
        timeout: TimeSpan.FromMinutes(10),
        log: Console.WriteLine);

    var manifest = AgentVersionManifest.Load(settings.ManifestPath);
    await publisher.PublishAsync(specs, settings.RepositoryRoot, manifest, cancellation.Token);
    manifest.Save(settings.ManifestPath);

    Console.WriteLine();
    Console.WriteLine($"Published versions (recorded in {settings.ManifestPath}):");
    foreach (var (name, version) in manifest.Versions)
    {
        Console.WriteLine($"  {name} = {version}");
    }

    Console.WriteLine();
    Console.WriteLine("Point the bot at them so a later `azd provision` keeps them:");
    foreach (var command in manifest.ToAzdEnvCommands())
    {
        Console.WriteLine($"  {command}");
    }

    Console.WriteLine();
    Console.WriteLine("Or set them on the running bot directly (no provision):");
    Console.WriteLine($"  az webapp config appsettings set -g <resource-group> -n <bot-app> --settings {string.Join(' ', manifest.ToBotAppSettings())}");
    return 0;
}
catch (InvalidOperationException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("Cancelled.");
    return 130;
}
