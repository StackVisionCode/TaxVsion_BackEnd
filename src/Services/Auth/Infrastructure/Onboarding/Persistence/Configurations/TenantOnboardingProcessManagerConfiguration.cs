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

        // F12 había marcado esto Ignore() asumiendo que "no tiene motivo para tocar SQL" — falso: en
        // un Saga EF-backed de Wolverine, Start() y Handle(TenantCreatedForOnboardingIntegrationEvent)
        // corren en procesos de mensaje separados (Tenant service responde de forma asíncrona), así
        // que la fila de OnboardingSagas ES el único estado que sobrevive entre ambos pasos. Con
        // Ignore(), SaveChangesAsync tras Start() nunca persistía el valor y la siguiente carga del
        // saga desde SQL lo traía null, tirando InvalidOperationException en PasswordHashReference!.Value
        // — bug real, encontrado corriendo el onboarding E2E de punta a punta (100% de los registros
        // completos se caían acá). Se vuelve a mapear como columna normal; sigue siendo sólo una
        // referencia Redis GETDEL de un solo uso (nunca el hash en sí), y la Saga la pone a null y
        // guarda inmediatamente después de consumirla en Handle(TenantCreatedForOnboardingIntegrationEvent).
        builder.Property(saga => saga.PasswordHashReference);
    }
}
