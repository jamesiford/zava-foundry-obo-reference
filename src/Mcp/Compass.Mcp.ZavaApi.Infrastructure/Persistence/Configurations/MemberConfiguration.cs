using Compass.Mcp.ZavaApi.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Compass.Mcp.ZavaApi.Infrastructure.Persistence.Configurations;

internal sealed class MemberConfiguration : IEntityTypeConfiguration<Member>
{
    public void Configure(EntityTypeBuilder<Member> builder)
    {
        builder.ToTable("Members");
        builder.HasKey(m => m.MemberNumber);
        builder.Property(m => m.MemberNumber).HasMaxLength(32);
        builder.Property(m => m.FirstName).HasMaxLength(100);
        builder.Property(m => m.LastName).HasMaxLength(100);
        builder.Property(m => m.Email).HasMaxLength(256);
        builder.Property(m => m.JoinedOn);
        builder.Property(m => m.Status).HasConversion<string>().HasMaxLength(20);
    }
}
