using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Auth.Domain.Onboarding.SubdomainReservations;

namespace TaxVision.Auth.Infrastructure.Onboarding.Persistence.Configurations;

/// <summary>Mismo criterio que TenantSubdomainReservationConfiguration: no hay UNIQUE puro (un slug
/// liberado puede reservarse de nuevo), solo un índice compuesto que acelera la consulta de
/// "reserva activa" — la unicidad de "activa" la garantiza el repo/handler.</summary>
public sealed class OnboardingSubdomainReservationConfiguration
    : IEntityTypeConfiguration<OnboardingSubdomainReservation>
{
    public void Configure(EntityTypeBuilder<OnboardingSubdomainReservation> builder)
    {
        builder.ToTable("OnboardingSubdomainReservations");
        builder.HasKey(reservation => reservation.Id);

        builder.Property(reservation => reservation.Slug).HasMaxLength(63).IsRequired();
        builder.Property(reservation => reservation.OnboardingId).IsRequired();
        builder.Property(reservation => reservation.ReservedByEmail).HasMaxLength(256).IsRequired();
        builder.Property(reservation => reservation.CreatedAtUtc).IsRequired();
        builder.Property(reservation => reservation.ExpiresAtUtc).IsRequired();

        builder.HasIndex(reservation => new
        {
            reservation.Slug,
            reservation.ConsumedAtUtc,
            reservation.ExpiresAtUtc,
        });

        builder.HasIndex(reservation => reservation.OnboardingId);
    }
}
