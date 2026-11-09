using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Tenancy;
using TaxVision.Auth.Onboarding.Application.Abstractions;
using TaxVision.Auth.Onboarding.Domain.TenantOnboardings;

namespace TaxVision.Auth.Onboarding.Application.Sagas.Commands;

public sealed record CreateTenantForOnboardingCommand(
    Guid OnboardingId,
    string OfficeName,
    string Subdomain,
    string AdminEmail,
    DateTime PaymentCompletedAtUtc
);

/// <summary>PayFlow (Fase 15) — primer paso de la Saga: dispara la creación del Tenant real vía M2M
/// fire-and-forget. La Saga avanza cuando le llega <c>TenantCreatedForOnboardingIntegrationEvent</c>
/// (Fase 16) por el bus, no por la respuesta HTTP de este handler. Si el propio despacho M2M falla
/// (red, 5xx), publica <see cref="OnboardingProvisioningStepFailedIntegrationEvent"/> de inmediato
/// para que la Saga registre el fallo sin esperar un timeout.</summary>
public static class CreateTenantForOnboardingHandler
{
    public static async Task<OnboardingProvisioningStepFailedIntegrationEvent?> Handle(
        CreateTenantForOnboardingCommand command,
        ITenantProvisioningClient client,
        ITenantOnboardingRepository onboardings,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        // La zona del navegador vive en el registro, no en el evento que arranca la saga; si no está,
        // Tenant crea la oficina en UTC (también para onboardings en vuelo sin el dato).
        var timeZoneId = (await onboardings.GetByIdAsync(command.OnboardingId, ct))?.TimeZoneId;

        var result = await client.CreateTenantAsync(
            new CreateTenantForOnboardingRequest(
                command.OnboardingId,
                command.OfficeName,
                command.Subdomain,
                command.AdminEmail,
                command.PaymentCompletedAtUtc,
                timeZoneId
            ),
            ct
        );

        if (result.IsSuccess)
            return null;

        return new OnboardingProvisioningStepFailedIntegrationEvent
        {
            TenantId = PlatformTenant.Id,
            OnboardingId = command.OnboardingId,
            FailedStep = TenantProvisioningStep.Tenant.ToString(),
            FailureCode = result.Error.Code,
            FailureReason = result.Error.Message,
            CorrelationId = correlation.CorrelationId,
        };
    }
}
