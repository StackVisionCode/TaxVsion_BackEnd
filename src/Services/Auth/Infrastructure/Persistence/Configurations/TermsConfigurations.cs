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
        builder.Property(acceptance => acceptance.TermsVersionId).IsRequired();
        builder.Property(acceptance => acceptance.ContentHash).HasMaxLength(64);
        builder.Property(acceptance => acceptance.AcceptedInContext).HasMaxLength(32).IsRequired();
        // Columna DB se mantiene "IpAddress" (renombrada solo a nivel de C#, PayFlow Fase 6) para
        // no requerir un rename de columna en la migracion de retrofit.
        builder.Property(acceptance => acceptance.AcceptedFromIp).HasColumnName("IpAddress").HasMaxLength(45);
        builder.Property(acceptance => acceptance.UserAgent).HasMaxLength(512);
        builder.Property(acceptance => acceptance.AcceptedAtUtc).IsRequired();

        // GetLatestAsync ordena por AcceptedAtUtc descendente dentro del tenant.
        builder
            .HasIndex(acceptance => new { acceptance.TenantId, acceptance.AcceptedAtUtc })
            .IsDescending(false, true);

        // PayFlow Fase 6 — idempotencia: mismo usuario no debe tener 2 filas para la misma
        // TermsVersion dentro del mismo tenant. AcceptTermsHandler/AcceptTermsFromOnboardingHandler
        // verifican primero (check-then-insert) para que el flujo normal nunca choque contra este
        // indice; el indice es el backstop de una carrera real entre 2 requests concurrentes.
        builder
            .HasIndex(acceptance => new
            {
                acceptance.TenantId,
                acceptance.AcceptedByUserId,
                acceptance.TermsVersionId,
            })
            .IsUnique();
    }
}
