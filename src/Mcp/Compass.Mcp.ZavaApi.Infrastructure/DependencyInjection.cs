using Compass.Mcp.ZavaApi.Application.Abstractions;
using Compass.Mcp.ZavaApi.Infrastructure.Persistence;
using Compass.Mcp.ZavaApi.Infrastructure.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Compass.Mcp.ZavaApi.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Registers the SQLite-backed adapter for <see cref="IMemberAccountRepository"/>.</summary>
    public static IServiceCollection AddZavaApiInfrastructure(this IServiceCollection services, string? connectionString = null)
    {
        var resolved = connectionString ?? new ZavaApiStoreOptions().ConnectionString;

        services.TryAddSingleton(TimeProvider.System);
        services.AddDbContext<ZavaApiDbContext>(o => o.UseSqlite(resolved));
        services.AddScoped<IMemberAccountRepository, MemberAccountRepository>();
        services.AddScoped<ZavaApiDatabaseInitializer>();
        return services;
    }
}
