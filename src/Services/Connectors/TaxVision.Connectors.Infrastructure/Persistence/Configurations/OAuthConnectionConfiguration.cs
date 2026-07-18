using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Connectors.Domain.Accounts;

namespace TaxVision.Connectors.Infrastructure.Persistence.Configurations;

public sealed class OAuthConnectionConfiguration : IEntityTypeConfiguration<OAuthConnection>
{
    public void Configure(EntityTypeBuilder<OAuthConnection> builder)
    {
        builder.ToTable("OAuthConnections");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.AccountId).IsRequired();
        builder.Property(c => c.ProviderCode).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(c => c.ClientId).HasMaxLength(200).IsRequired();
        builder.Property(c => c.Scope).IsRequired();
        builder.Property(c => c.AuthorizedAtUtc).IsRequired();
        builder.Property(c => c.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(c => c.RevokedAtUtc);

        builder.HasIndex(c => c.AccountId).IsUnique();

        builder
            .HasOne(c => c.Token)
            .WithOne()
            .HasForeignKey<OAuthToken>(t => t.ConnectionId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(c => c.Token).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
