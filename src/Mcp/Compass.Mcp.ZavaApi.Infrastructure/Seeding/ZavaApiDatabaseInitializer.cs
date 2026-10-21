using Compass.Mcp.ZavaApi.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Compass.Mcp.ZavaApi.Infrastructure.Seeding;

/// <summary>
/// Creates the schema and seeds it once. Separate from the DbContext so seeding stays an explicit startup step
/// rather than a hidden side effect of the first query.
/// </summary>
public sealed class ZavaApiDatabaseInitializer(ZavaApiDbContext db, TimeProvider clock)
{
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await db.Database.EnsureCreatedAsync(cancellationToken);

        if (await db.Members.AnyAsync(cancellationToken))
        {
            return;
        }

        var seed = ZavaApiSeedData.Create(clock.GetUtcNow().UtcDateTime);
        db.Members.AddRange(seed.Members);
        db.Accounts.AddRange(seed.Accounts);
        db.Transactions.AddRange(seed.Transactions);
        await db.SaveChangesAsync(cancellationToken);
    }
}
