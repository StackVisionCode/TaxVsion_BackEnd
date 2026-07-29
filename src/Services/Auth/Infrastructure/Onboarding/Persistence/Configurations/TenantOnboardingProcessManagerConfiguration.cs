using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Auth.Application.Onboarding.Sagas;

namespace TaxVision.Auth.Infrastructure.Onboarding.Persistence.Configurations;

/// <summary>PayFlow (Fase 15) — mapeo EF Core estándar del primer Wolverine <c>Saga</c> del repo. No
/// requiere ningún wiring especial de Wolverine: la persistencia de sagas vía EF Core es automática
/// una vez que el tipo tiene un mapeo en un <c>DbContext</c> ya integrado con
/// <c>UseEntityFrameworkCoreTransactions()</c> (ya configurado en <c>Program.cs</c>).</summary>
public sealed class TenantOnboardingProcessManagerConfiguration
    : IEntityTypeConfiguration<TenantOnboardingProcessManager>
{
    public void Configure(EntityTypeBuilder<TenantOnboardingProcessManager> builder)
    {
        builder.ToTable("OnboardingSagas");
        builder.HasKey(saga => saga.Id);
        builder.Property(saga => saga.Id).ValueGeneratedNever();

        builder.Property(saga => saga.Email).HasMaxLength(256).IsRequired();
        builder.Property(saga => saga.FirstName).HasMaxLength(128).IsRequired();
        builder.Property(saga => saga.LastName).HasMaxLength(128).IsRequired();
        builder.Property(saga => saga.PlanId).IsRequired();
        builder.Property(saga => saga.Version);
    }
}
