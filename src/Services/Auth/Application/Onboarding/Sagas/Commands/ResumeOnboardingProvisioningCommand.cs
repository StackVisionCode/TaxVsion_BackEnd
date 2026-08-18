using Wolverine.Persistence.Sagas;

namespace TaxVision.Auth.Application.Onboarding.Sagas.Commands;

/// <summary>PayFlow (Fase 17) — dispatchado por <c>ResumeOnboardingAdminHandler</c> (acción manual del
/// admin) o por <c>OnboardingRetryScheduler</c> (reintento automático de un fallo Transient). Se rutea
/// a la instancia viva de <see cref="Sagas.TenantOnboardingProcessManager"/> vía
/// <see cref="SagaIdentityAttribute"/> — solo la Saga tiene el estado en memoria (Email/OfficeName/etc)
/// necesario para reconstruir el comando M2M exacto que falló.</summary>
public sealed record ResumeOnboardingProvisioningCommand([property: SagaIdentity] Guid OnboardingId);
