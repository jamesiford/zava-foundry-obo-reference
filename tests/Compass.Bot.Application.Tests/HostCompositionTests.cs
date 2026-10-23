using Compass.Bot.Application.Abstractions;
using Compass.Bot.Host;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Compass.Bot.Application.Tests;

/// <summary>
/// No other test boots the host's wiring, so a dropped registration in Program.cs passed every test and failed only at
/// runtime. This builds the real composition (<c>AddCompassBot</c>, the one call Program.cs makes) with scope and build
/// validation on, and resolves the orchestrator.
/// </summary>
public sealed class HostCompositionTests
{
    [Fact]
    public void The_hosts_composition_builds_with_validation_and_resolves_the_orchestrator()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Bot:Obo:TenantId"] = "tenant",
                ["Bot:Obo:ClientId"] = "client",
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddCompassBot(configuration);

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        using var scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IBriefingOrchestrator>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IAnswerFromExtracts>());
    }
}
