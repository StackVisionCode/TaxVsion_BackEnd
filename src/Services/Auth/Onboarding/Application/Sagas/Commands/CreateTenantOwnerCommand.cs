using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using Microsoft.Extensions.Logging;
using TaxVision.Auth.Onboarding.Application.Internal.Commands;
using TaxVision.Auth.Onboarding.Domain.TenantOnboardings;
using Wolverine;

namespace TaxVision.Auth.Onboarding.Application.Sagas.Commands;

public sealed record CreateTenantOwnerCommand(
    Guid OnboardingId,
    Guid TenantId,
    string Email,
    string FirstName,
    string LastName,
    Guid PasswordHashReference
);

/// <summary>PayFlow (Fase 15) — segundo paso de la Saga: crea el TenantAdmin en proceso, con
/// <c>InvokeAsync</c>. El comando no se serializa y solo lleva la referencia al hash, nunca el password.
/// La Saga avanza cuando le llega <c>TenantOwnerCreatedIntegrationEvent</c>; cualquier fallo, también una
/// excepción, se le avisa como paso fallido para que el onboarding vaya a ManualReview.</summary>
public static class CreateTenantOwnerHandler
{
    public static async Task<OnboardingProvisioningStepFailedIntegrationEvent?> Handle(
        CreateTenantOwnerCommand command,
        IMessageBus bus,
        ICorrelationContext correlation,
        ILogger<CreateTenantOwnerCommand> logger,
        CancellationToken ct
    )
    {
        var result = await CreateOwnerAsync(command, bus, logger, ct);
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

    private static async Task<Result> CreateOwnerAsync(
        CreateTenantOwnerCommand command,
        IMessageBus bus,
        ILogger logger,
        CancellationToken ct
    )
    {
        try
        {
            return await bus.InvokeAsync<Result>(
                new CreateTenantOwnerFromOnboardingCommand(
                    command.OnboardingId,
                    command.TenantId,
                    command.Email,
                    command.FirstName,
                    command.LastName,
                    command.PasswordHashReference
                ),
                ct
            );
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Owner creation failed for onboarding {OnboardingId}.", command.OnboardingId);
            return Result.Failure(new Error("Onboarding.OwnerCreationFailed", "Could not create the office owner."));
        }
    }
}
