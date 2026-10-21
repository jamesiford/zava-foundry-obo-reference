using Compass.Bot.Host.FrontDoor;
using Compass.Bot.Infrastructure.Obo;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Compass.IdentityIsolation.Tests.Support;

/// <summary>
/// Starts the Host's real front-door endpoints (<c>MapFrontDoor</c>) on a test server, so the tests exercise the same
/// handler, failure mapping and SSE writer that ship, with only the services behind the <c>ISender</c> swapped.
/// </summary>
internal static class FrontDoorHost
{
    public const string FakeClientId = "11111111-aaaa-bbbb-cccc-000000000001";

    public const string FakeTenantId = "22222222-aaaa-bbbb-cccc-000000000002";

    public static async Task<WebApplication> StartAsync(Action<IServiceCollection> registerServices, Action<WebApplicationBuilder>? configure = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        configure?.Invoke(builder);

        registerServices(builder.Services);
        builder.Services.AddFrontDoor(builder.Configuration);
        builder.Services.Configure<OnBehalfOfOptions>(o =>
        {
            o.ClientId = FakeClientId;
            o.TenantId = FakeTenantId;
        });

        var app = builder.Build();
        app.MapFrontDoor();
        await app.StartAsync();
        return app;
    }
}
