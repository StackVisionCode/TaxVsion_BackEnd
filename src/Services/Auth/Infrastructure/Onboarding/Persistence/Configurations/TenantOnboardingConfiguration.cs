using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Auth.Domain.Onboarding.TenantOnboardings;

namespace TaxVision.Auth.Infrastructure.Onboarding.Persistence.Configurations;

public sealed class TenantOnboardingConfiguration : IEntityTypeConfiguration<TenantOnboarding>
{
    public void Configure(EntityTypeBuilder<TenantOnboarding> builder)
    {
        builder.ToTable("TenantOnboardings");
        builder.HasKey(onboarding => onboarding.Id);

        builder.Property(onboarding => onboarding.FirstName).HasMaxLength(128).IsRequired();
        builder.Property(onboarding => onboarding.LastName).HasMaxLength(128).IsRequired();
        builder.Property(onboarding => onboarding.Email).HasMaxLength(256).IsRequired();
        builder.Property(onboarding => onboarding.EmailVerifiedAtUtc).IsRequired();
        builder.Property(onboarding => onboarding.Phone).HasMaxLength(32);
        builder.Property(onboarding => onboarding.PlanId).IsRequired();
        builder
            .Property(onboarding => onboarding.BillingCycle)
            .HasMaxLength(24)
            .IsRequired()
            .HasDefaultValue("Monthly");
        builder.Property(onboarding => onboarding.Status).HasConversion<string>().HasMaxLength(24).IsRequired();

        builder.Property(onboarding => onboarding.PaymentStatus).HasMaxLength(24);
        builder.Property(onboarding => onboarding.PaymentReference).HasMaxLength(128);

        builder.Property(onboarding => onboarding.RegistrationTokenHash).HasMaxLength(64);

        builder.Property(onboarding => onboarding.OfficeName).HasMaxLength(256);
        builder.Property(onboarding => onboarding.RequestedSubdomain).HasMaxLength(63);

        builder.Property(onboarding => onboarding.TermsContentHash).HasMaxLength(64);
        builder.Property(onboarding => onboarding.AcceptedFromIp).HasMaxLength(45);
        builder.Property(onboarding => onboarding.UserAgent).HasMaxLength(512);

        builder.Property(onboarding => onboarding.CreatedAtUtc).IsRequired();

        // Gift/Referral: desglose comercial congelado + reservas de código apiladas.
        builder.Property(onboarding => onboarding.Currency).HasMaxLength(3);
        builder.Property(onboarding => onboarding.FullyCovered).IsRequired();
        // Entidad NORMAL (no owned): al agregar un hijo nuevo a un agregado YA cargado, EF lo marca
        // Added→INSERT de forma predecible. Como owned (OwnsMany) EF lo emitía como UPDATE (0 filas) →
        // DbUpdateConcurrencyException. La config del hijo vive en OnboardingCodeReservationConfiguration.
        builder
            .HasMany(onboarding => onboarding.CodeReservations)
            .WithOne()
            .HasForeignKey(reservation => reservation.OnboardingId)
            .OnDelete(DeleteBehavior.Cascade);
        builder
            .Metadata.FindNavigation(nameof(TenantOnboarding.CodeReservations))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.Property(onboarding => onboarding.CurrentStep).HasConversion<string>().HasMaxLength(24).IsRequired();
        builder.Property(onboarding => onboarding.FailedStep).HasConversion<string>().HasMaxLength(24);
        builder.Property(onboarding => onboarding.FailureCode).HasMaxLength(128);
        builder.Property(onboarding => onboarding.FailureReason).HasMaxLength(1024);

        // PayFlow (Fase 17) — retry automático de fallos Transient.
        builder.Property(onboarding => onboarding.RetryAttempt).IsRequired();
        builder.Property(onboarding => onboarding.NextRetryAtUtc);

        builder
            .HasIndex(onboarding => onboarding.RegistrationTokenHash)
            .IsUnique()
            .HasFilter("[RegistrationTokenHash] IS NOT NULL");

        builder.HasIndex(onboarding => new { onboarding.Email, onboarding.Status });
        builder.HasIndex(onboarding => new { onboarding.Status, onboarding.CreatedAtUtc });

        builder
            .HasIndex(onboarding => onboarding.NextRetryAtUtc)
            .HasFilter("[NextRetryAtUtc] IS NOT NULL")
            .HasDatabaseName("IX_TenantOnboardings_NextRetryAtUtc");
    }
}
