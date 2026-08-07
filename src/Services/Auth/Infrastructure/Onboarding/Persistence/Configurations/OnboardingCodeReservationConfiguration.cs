using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Auth.Domain.Onboarding.TenantOnboardings;

namespace TaxVision.Auth.Infrastructure.Onboarding.Persistence.Configurations;

/// <summary>Reserva de código apilada del onboarding — entidad NORMAL (no owned) para que EF trackee
/// correctamente los hijos nuevos agregados a un agregado ya cargado (Added→INSERT). Misma tabla/FK que
/// la versión owned previa; no requiere migración de esquema.</summary>
public sealed class OnboardingCodeReservationConfiguration : IEntityTypeConfiguration<OnboardingCodeReservation>
{
    public void Configure(EntityTypeBuilder<OnboardingCodeReservation> b)
    {
        b.ToTable("OnboardingCodeReservations");
        b.HasKey(r => r.Id);
        // PK asignado por la app (BaseEntity = Guid.NewGuid).
        b.Property(r => r.Id).ValueGeneratedNever();
        b.Property(r => r.OnboardingId).IsRequired();
        b.Property(r => r.BenefitType).HasConversion<string>().HasMaxLength(16).IsRequired();
        b.Property(r => r.Code).HasMaxLength(64);
        b.Property(r => r.SnapshotHash).HasMaxLength(128).IsRequired();
        b.HasIndex(r => r.CodeReservationId);
        b.HasIndex(r => r.OnboardingId);
    }
}
