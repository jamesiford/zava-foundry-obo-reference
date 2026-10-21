using Azure.Monitor.OpenTelemetry.Exporter;
using Compass.Mcp.ZavaApi.Application;
using Compass.Mcp.ZavaApi.Host.Authentication;
using Compass.Mcp.ZavaApi.Host.Tools;
using Compass.Mcp.ZavaApi.Infrastructure;
using Compass.Mcp.ZavaApi.Infrastructure.Persistence;
using Compass.Mcp.ZavaApi.Infrastructure.Seeding;
using Compass.Shared.Identity.Tokens;
using Compass.Shared.Observability;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Azure.Functions.Worker.OpenTelemetry;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry;

var builder = FunctionsApplication.CreateBuilder(args);

// Redaction first: processors run in registration order, so it must precede every exporter (OTLP from ServiceDefaults
// and Azure Monitor below).
builder.Services.AddTokenRedaction();

// Same Aspire defaults as the bot: OpenTelemetry, health checks and service discovery, so the dashboard shows both.
builder.AddServiceDefaults();

builder.ConfigureFunctionsWebApplication();

// Must come after ConfigureFunctionsWebApplication: the MCP extension's middleware publishes the invocation
// context (request headers) that the token check reads, so it has to run first.
builder.UseMiddleware<ZavaApiTokenValidationMiddleware>();

builder.Services
    .AddZavaApiApplication()
    .AddZavaApiInfrastructure(builder.Configuration[$"{ZavaApiStoreOptions.SectionName}:ConnectionString"])
    .AddJwtAccessTokenValidation(o => builder.Configuration.GetSection(JwtValidationOptions.SectionName).Bind(o))
    .AddScoped<ToolResultRunner>();

if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("APPLICATIONINSIGHTS_CONNECTION_STRING")))
{
    builder.Services.AddOpenTelemetry()
        .UseFunctionsWorkerDefaults()
        .UseAzureMonitorExporter();
}

var host = builder.Build();

// The simulated core-banking store is disposable seed data, so (re)creating it at startup is safe.
using (var scope = host.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<ZavaApiDatabaseInitializer>().InitializeAsync();
}

await host.RunAsync();