using Compass.Mcp.ZavaApi.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Compass.Mcp.ZavaApi.Infrastructure.Persistence.Configurations;

internal sealed class AccountConfiguration : IEntityTypeConfiguration<Account>
{
    public void Configure(EntityTypeBuilder<Account> builder)
    {
        builder.ToTable("Accounts");
        builder.HasKey(a => a.AccountNumber);
        builder.Property(a => a.AccountNumber).HasMaxLength(32);
        builder.Property(a => a.MemberNumber).HasMaxLength(32);
        builder.Property(a => a.Nickname).HasMaxLength(100);
        builder.Property(a => a.Currency).HasMaxLength(3);
        builder.Property(a => a.Type).HasConversion<string>().HasMaxLength(30);
        builder.Property(a => a.Balance).HasPrecision(18, 2);

        // Get-only properties aren't mapped by convention, so every column is declared explicitly.
        builder.Property(a => a.OpenedOn);

        builder.HasOne<Member>().WithMany().HasForeignKey(a => a.MemberNumber).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(a => a.MemberNumber);
    }
}
