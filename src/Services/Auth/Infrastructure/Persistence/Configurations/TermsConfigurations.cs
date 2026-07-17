using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Auth.Domain.Terms;

namespace TaxVision.Auth.Infrastructure.Persistence.Configurations;

/// <summary>Mapeo EF Core de TenantTermsAcceptance: historial append-only de aceptaciones del ToS/AUP por tenant.</summary>
public sealed class TenantTermsAcceptanceConfiguration : IEntityTypeConfiguration<TenantTermsAcceptance>
{
    public void Configure(EntityTypeBuilder<TenantTermsAcceptance> builder)
    {
        builder.ToTable("TenantTermsAcceptances");
        builder.HasKey(acceptance => acceptance.Id);
        builder.Property(acceptance => acceptance.TenantId).IsRequired();
        builder.Property(acceptance => acceptance.AcceptedByUserId).IsRequired();
        builder.Property(acceptance => acceptance.TermsVersion).HasMaxLength(32).IsRequired();
        builder.Property(acceptance => acceptance.IpAddress).HasMaxLength(45);
        builder.Property(acceptance => acceptance.UserAgent).HasMaxLength(512);
        builder.Property(acceptance => acceptance.AcceptedAtUtc).IsRequired();

        // GetLatestAsync ordena por AcceptedAtUtc descendente dentro del tenant.
        builder
            .HasIndex(acceptance => new { acceptance.TenantId, acceptance.AcceptedAtUtc })
            .IsDescending(false, true);
    }
}
