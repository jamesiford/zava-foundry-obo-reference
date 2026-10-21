using Compass.Mcp.ZavaApi.Application;
using Compass.Mcp.ZavaApi.Application.Abstractions;
using Compass.Shared.Messaging;
using Compass.Mcp.ZavaApi.Infrastructure.Persistence;
using Compass.Mcp.ZavaApi.Infrastructure.Seeding;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Compass.Mcp.ZavaApi.Tests.Support;

/// <summary>
/// The real handlers, sender and repository wired over an in-memory SQLite database seeded by the production
/// initializer — so handler tests exercise the same data shape and query translation as the running server.
/// </summary>
public sealed class ZavaApiTestHost : IAsyncDisposable
{
    /// <summary>Fixed clock so seeded transaction dates are identical on every run.</summary>
    public static readonly DateTimeOffset SeedTime = new(2026, 3, 15, 12, 0, 0, TimeSpan.Zero);

    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _provider;
    private readonly IServiceScope _scope;

    private ZavaApiTestHost(SqliteConnection connection, ServiceProvider provider)
    {
        _connection = connection;
        _provider = provider;
        _scope = provider.CreateScope();
    }

    public ISender Sender => _scope.ServiceProvider.GetRequiredService<ISender>();

    public IMemberAccountRepository Repository => _scope.ServiceProvider.GetRequiredService<IMemberAccountRepository>();

    /// <summary>A fresh scope over the same database, for tests that need a second unit of work.</summary>
    public IServiceScope CreateScope() => _provider.CreateScope();

    public static async Task<ZavaApiTestHost> CreateAsync()
    {
        // The in-memory database lives only as long as this open connection.
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(new FakeTimeProvider(SeedTime));
        services.AddDbContext<ZavaApiDbContext>(o => o.UseSqlite(connection));
        services.AddScoped<IMemberAccountRepository, MemberAccountRepository>();
        services.AddScoped<ZavaApiDatabaseInitializer>();
        services.AddZavaApiApplication();

        var provider = services.BuildServiceProvider();
        await using (var scope = provider.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<ZavaApiDatabaseInitializer>().InitializeAsync();
        }

        return new ZavaApiTestHost(connection, provider);
    }

    public async ValueTask DisposeAsync()
    {
        _scope.Dispose();
        await _provider.DisposeAsync();
        await _connection.DisposeAsync();
    }
}
