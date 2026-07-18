using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Postmaster.Domain.Projections;

namespace TaxVision.Postmaster.Infrastructure.Persistence.Configurations;

internal sealed class TenantOAuthAccountConfiguration : IEntityTypeConfiguration<TenantOAuthAccount>
{
    public void Configure(EntityTypeBuilder<TenantOAuthAccount> builder)
    {
        builder.ToTable("TenantOAuthAccounts");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.TenantId).IsRequired();
        builder.Property(a => a.AccountId).IsRequired();
        builder.Property(a => a.ProviderCode).IsRequired().HasMaxLength(50);
        builder.Property(a => a.FromAddress).IsRequired().HasMaxLength(320);
        builder.Property(a => a.IsActive).IsRequired();
        builder.Property(a => a.ConnectedAtUtc).IsRequired();
        builder.Property(a => a.DisconnectedAtUtc);
        builder.Property(a => a.UpdatedAtUtc).IsRequired();

        builder.HasIndex(a => new { a.TenantId, a.AccountId }).IsUnique();
        builder.HasIndex(a => new
        {
            a.TenantId,
            a.IsActive,
            a.ConnectedAtUtc,
        });
    }
}
