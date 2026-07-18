using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Auth.Domain.TenantDomains;

namespace TaxVision.Auth.Infrastructure.Persistence.Configurations;

/// <summary>Mapeo EF Core de TenantDomain: host único globalmente, slug único filtrado (solo subdominios).</summary>
public sealed class TenantDomainConfiguration : IEntityTypeConfiguration<TenantDomain>
{
    public void Configure(EntityTypeBuilder<TenantDomain> builder)
    {
        builder.ToTable("TenantDomains");
        builder.HasKey(domain => domain.Id);

        builder.Property(domain => domain.TenantId).IsRequired();
        builder.Property(domain => domain.DomainType).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(domain => domain.Host).HasMaxLength(253).IsRequired();
        builder.Property(domain => domain.SubdomainSlug).HasMaxLength(63);
        builder.Property(domain => domain.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(domain => domain.IsPrimary).IsRequired();
        builder.Property(domain => domain.CloudflareCustomHostnameId).HasMaxLength(64);
        builder.Property(domain => domain.VerificationMethod).HasMaxLength(20);
        builder.Property(domain => domain.CreatedByUserId).IsRequired();
        builder.Property(domain => domain.CreatedAtUtc).IsRequired();

        // Host es único GLOBALMENTE (no por tenant): dos oficinas nunca pueden compartir
        // ni subdominio ni dominio propio — es la clave de resolución Host->TenantId (A3).
        builder.HasIndex(domain => domain.Host).IsUnique();

        // Slug único filtrado: solo aplica a subdominios (los custom hostnames no tienen slug).
        builder.HasIndex(domain => domain.SubdomainSlug).IsUnique().HasFilter("[SubdomainSlug] IS NOT NULL");

        builder.HasIndex(domain => new { domain.TenantId, domain.Status });
        builder.HasIndex(domain => new { domain.TenantId, domain.IsPrimary });
    }
}

/// <summary>Mapeo EF Core de TenantSubdomainReservation: reserva temporal única por slug activo.</summary>
public sealed class TenantSubdomainReservationConfiguration : IEntityTypeConfiguration<TenantSubdomainReservation>
{
    public void Configure(EntityTypeBuilder<TenantSubdomainReservation> builder)
    {
        builder.ToTable("TenantSubdomainReservations");
        builder.HasKey(reservation => reservation.Id);

        builder.Property(reservation => reservation.SubdomainSlug).HasMaxLength(63).IsRequired();
        builder.Property(reservation => reservation.ReservedByEmail).HasMaxLength(256).IsRequired();
        builder.Property(reservation => reservation.CreatedAtUtc).IsRequired();
        builder.Property(reservation => reservation.ExpiresAtUtc).IsRequired();

        // No es UNIQUE puro: un slug liberado (expirado o consumido) puede reservarse de
        // nuevo. La unicidad de "activa" se aplica en el repo (WHERE ConsumedAtUtc IS NULL
        // AND ExpiresAtUtc > now) — este índice solo acelera esa consulta.
        builder.HasIndex(reservation => new
        {
            reservation.SubdomainSlug,
            reservation.ConsumedAtUtc,
            reservation.ExpiresAtUtc,
        });
    }
}
