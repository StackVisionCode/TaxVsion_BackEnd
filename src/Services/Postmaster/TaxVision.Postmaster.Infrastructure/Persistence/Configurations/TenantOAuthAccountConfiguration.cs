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
        // Id es Guid client-generado (Guid.NewGuid() en ForNewConnection) — ver SystemEmailProviderConfiguration.
        // Especialmente relevante acá: se crea al conectar y se actualiza al desconectar (consumers de
        // connectors.tenant_email_account.*), el patrón exacto de doble-touch que dispara el bug.
        builder.Property(a => a.Id).ValueGeneratedNever();

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
