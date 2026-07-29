using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Tenancy;
using TaxVision.Auth.Application.Onboarding.Abstractions;
using TaxVision.Auth.Domain.Onboarding.TenantOnboardings;

namespace TaxVision.Auth.Application.Onboarding.Sagas.Commands;

public sealed record ActivateSubdomainForTenantCommand(
    Guid OnboardingId,
    Guid TenantId,
    Guid UserId,
    Guid SubscriptionId
);

/// <summary>PayFlow (Fase 15) — paso "Subdomain" de la Saga. No dispara M2M: el subdominio base
/// (<c>{slug}.taxprocore.com</c>) ya queda activo en el momento en que Tenant crea el aggregate con
/// <c>Tenant.SubDomain</c> seteado (Fase 16, <c>CreateTenantFromOnboardingCommand</c>) — a diferencia
/// del flujo de dominios personalizados (<c>TenantDomains</c>/Cloudflare), que es una feature
/// separada y no aplica acá. Este handler solo avanza el <see cref="TenantOnboarding"/> (UoW local)
/// y encadena al último paso. Fase 17: publica <see cref="OnboardingProvisioningStepFailedIntegrationEvent"/>
/// en vez de descartar en silencio un fallo — mismo motivo que <c>ProvisionStorageForTenantHandler</c>.</summary>
public static class ActivateSubdomainForTenantHandler
{
    public static async Task<object?> Handle(
        ActivateSubdomainForTenantCommand command,
        ITenantOnboardingRepository onboardings,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var onboarding = await onboardings.GetByIdAsync(command.OnboardingId, ct);
        if (onboarding is null)
            return null;

        var result = onboarding.MarkStepCompleted(TenantProvisioningStep.Subdomain);
        if (result.IsFailure)
        {
            return new OnboardingProvisioningStepFailedIntegrationEvent
            {
                TenantId = PlatformTenant.Id,
                OnboardingId = command.OnboardingId,
                FailedStep = TenantProvisioningStep.Subdomain.ToString(),
                FailureCode = result.Error.Code,
                FailureReason = result.Error.Message,
                CorrelationId = correlation.CorrelationId,
            };
        }

        await unitOfWork.SaveChangesAsync(ct);

        return new ConfigureTenantDefaultsCommand(
            command.OnboardingId,
            command.TenantId,
            command.UserId,
            command.SubscriptionId
        );
    }
}
