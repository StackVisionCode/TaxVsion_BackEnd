using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Auth.Application.Onboarding.Abstractions;
using Wolverine;

namespace TaxVision.Auth.Application.Onboarding.Admin.Commands;

public sealed record CancelAndRefundOnboardingAdminCommand(Guid OnboardingId, string Reason, string Confirmation);

/// <summary>PayFlow (Fase 17) — receptor de <c>POST /auth/onboarding/admin/{id}/cancel-and-refund</c>.
/// Exige el texto de confirmación exacto (plan Fase 17) — evita un refund accidental por un click
/// perdido en un botón de admin. Publica <see cref="OnboardingRefundRequestedIntegrationEvent"/>
/// (PaymentApp ejecuta el refund real vía Stripe) y <see cref="OnboardingCancelRequestedIntegrationEvent"/>
/// (compensa Tenant/Auth/Subscription si ya llegaron a existir) — el orden importa: ambos se publican
/// DESPUÉS de persistir <c>MarkRefunded</c>, nunca antes, para que un fallo de PublishAsync no deje
/// el onboarding en un estado inconsistente con lo que ya se le prometió al cliente.</summary>
public static class CancelAndRefundOnboardingAdminHandler
{
    private const string RequiredConfirmation = "I understand this is irreversible";

    public static async Task<Result> Handle(
        CancelAndRefundOnboardingAdminCommand command,
        ITenantOnboardingRepository onboardings,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        if (string.IsNullOrWhiteSpace(command.Reason))
            return Result.Failure(new Error("Onboarding.CancelReasonRequired", "A reason is required."));

        if (command.Confirmation != RequiredConfirmation)
        {
            return Result.Failure(
                new Error(
                    "Onboarding.ConfirmationRequired",
                    $"Confirmation text must be exactly: \"{RequiredConfirmation}\"."
                )
            );
        }

        var onboarding = await onboardings.GetByIdAsync(command.OnboardingId, ct);
        if (onboarding is null)
            return Result.Failure(new Error("Onboarding.NotFound", "Onboarding not found."));

        if (onboarding.PaymentId is null)
            return Result.Failure(
                new Error("Onboarding.NoPayment", "This onboarding has no associated payment to refund.")
            );

        var result = onboarding.MarkRefunded(command.Reason);
        if (result.IsFailure)
            return result;

        await unitOfWork.SaveChangesAsync(ct);

        await bus.PublishAsync(
            new OnboardingRefundRequestedIntegrationEvent
            {
                OnboardingId = command.OnboardingId,
                PaymentId = onboarding.PaymentId.Value,
                Reason = command.Reason,
                CorrelationId = correlation.CorrelationId,
            }
        );

        if (onboarding.TenantId is not null || onboarding.UserId is not null || onboarding.SubscriptionId is not null)
        {
            await bus.PublishAsync(
                new OnboardingCancelRequestedIntegrationEvent
                {
                    OnboardingId = command.OnboardingId,
                    Reason = command.Reason,
                    OnboardingTenantId = onboarding.TenantId,
                    OnboardingUserId = onboarding.UserId,
                    OnboardingSubscriptionId = onboarding.SubscriptionId,
                    CorrelationId = correlation.CorrelationId,
                }
            );
        }

        return Result.Success();
    }
}
