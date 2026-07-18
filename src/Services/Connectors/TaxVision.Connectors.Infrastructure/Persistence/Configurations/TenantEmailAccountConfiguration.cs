using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Connectors.Domain.Accounts;

namespace TaxVision.Connectors.Infrastructure.Persistence.Configurations;

public sealed class TenantEmailAccountConfiguration : IEntityTypeConfiguration<TenantEmailAccount>
{
    public void Configure(EntityTypeBuilder<TenantEmailAccount> builder)
    {
        builder.ToTable("TenantEmailAccounts");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.TenantId).IsRequired();
        builder.Property(a => a.EmailAddress).HasMaxLength(320).IsRequired();
        builder.Property(a => a.ProviderCode).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(a => a.DisplayName).HasMaxLength(200);
        builder.Property(a => a.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(a => a.ConnectedAtUtc);
        builder.Property(a => a.LastActivityAtUtc).IsRequired();
        builder.Property(a => a.CreatedByUserId).IsRequired();
        builder.Property(a => a.CreatedAtUtc).IsRequired();

        builder.HasIndex(a => new { a.TenantId, a.EmailAddress }).IsUnique();

        // ReconciliationJob filtra únicamente por Status (sin TenantId — background job
        // system-level, ver ITenantEmailAccountRepository.ListActiveAsync) cada vez que corre.
        builder.HasIndex(a => a.Status);
    }
}
