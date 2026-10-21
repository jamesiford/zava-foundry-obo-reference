using Compass.Mcp.ZavaApi.Domain;
using Microsoft.EntityFrameworkCore;

namespace Compass.Mcp.ZavaApi.Infrastructure.Persistence;

/// <summary>
/// EF Core view of the simulated core-banking store. Internal to the adapter boundary: the Application layer sees
/// only <c>IMemberAccountRepository</c>, never this context.
/// </summary>
public sealed class ZavaApiDbContext(DbContextOptions<ZavaApiDbContext> options) : DbContext(options)
{
    public DbSet<Member> Members => Set<Member>();

    public DbSet<Account> Accounts => Set<Account>();

    public DbSet<Transaction> Transactions => Set<Transaction>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ZavaApiDbContext).Assembly);
}
