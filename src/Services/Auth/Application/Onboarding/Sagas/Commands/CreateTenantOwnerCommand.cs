using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Tenancy;
using TaxVision.Auth.Application.Onboarding.Abstractions;
using TaxVision.Auth.Domain.Onboarding.TenantOnboardings;

namespace TaxVision.Auth.Application.Onboarding.Sagas.Commands;

public sealed record CreateTenantOwnerCommand(
    Guid OnboardingId,
    Guid TenantId,
    string Email,
    string FirstName,
    string LastName,
    Guid PasswordHashReference
);

/// <summary>PayFlow (Fase 15) — segundo paso de la Saga: dispara la creación del TenantAdmin vía
/// loopback HTTP a <c>IAuthInternalOwnerCreationClient</c> (no un command local de Wolverine — el
/// password nunca debe cruzar el bus). Fire-and-forget: la Saga avanza cuando le llega
/// <c>TenantOwnerCreatedIntegrationEvent</c>.</summary>
public static class CreateTenantOwnerHandler
{
    public static async Task<OnboardingProvisioningStepFailedIntegrationEvent?> Handle(
        CreateTenantOwnerCommand command,
        IAuthInternalOwnerCreationClient client,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var result = await client.CreateOwnerAsync(
            new CreateTenantOwnerForOnboardingRequest(
                command.OnboardingId,
                command.TenantId,
                command.Email,
                command.FirstName,
                command.LastName,
                command.PasswordHashReference
            ),
            ct
        );

        if (result.IsSuccess)
            return null;

        return new OnboardingProvisioningStepFailedIntegrationEvent
        {
            TenantId = PlatformTenant.Id,
            OnboardingId = command.OnboardingId,
            FailedStep = TenantProvisioningStep.TenantAdmin.ToString(),
            FailureCode = result.Error.Code,
            FailureReason = result.Error.Message,
            CorrelationId = correlation.CorrelationId,
        };
    }
}
