using Compass.Mcp.ZavaApi.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Compass.Mcp.ZavaApi.Infrastructure.Persistence.Configurations;

internal sealed class TransactionConfiguration : IEntityTypeConfiguration<Transaction>
{
    public void Configure(EntityTypeBuilder<Transaction> builder)
    {
        builder.ToTable("Transactions");
        builder.HasKey(t => t.TransactionId);
        builder.Property(t => t.TransactionId).HasMaxLength(64);
        builder.Property(t => t.AccountNumber).HasMaxLength(32);
        builder.Property(t => t.Description).HasMaxLength(200);
        builder.Property(t => t.Type).HasConversion<string>().HasMaxLength(20);
        builder.Property(t => t.Amount).HasPrecision(18, 2);

        // SQLite hands DateTime back as Kind.Unspecified; the domain insists on UTC, so restore the kind on read.
        builder.Property(t => t.PostedAtUtc)
            .HasConversion(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

        builder.HasOne<Account>().WithMany().HasForeignKey(t => t.AccountNumber).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(t => new { t.AccountNumber, t.PostedAtUtc });
    }
}
