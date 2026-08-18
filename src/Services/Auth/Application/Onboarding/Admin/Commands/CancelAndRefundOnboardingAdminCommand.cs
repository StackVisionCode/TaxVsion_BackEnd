using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Common;
using TaxVision.Auth.Application.Onboarding.Abstractions;
using TaxVision.Auth.Domain.Audit;
using Wolverine;

namespace TaxVision.Auth.Application.Onboarding.Admin.Commands;

public sealed record CancelAndRefundOnboardingAdminCommand(
    Guid OnboardingId,
    string Reason,
    string Confirmation,
    Guid AdminUserId
);

/// <summary>PayFlow (Fase 17) — receptor de <c>POST /auth/onboarding/admin/{id}/cancel-and-refund</c>.
/// Exige el texto de confirmación exacto (plan Fase 17) — evita un refund accidental por un click
/// perdido en un botón de admin. Publica <see cref="OnboardingRefundRequestedIntegrationEvent"/>
/// (PaymentApp ejecuta el refund real vía Stripe) y <see cref="OnboardingCancelRequestedIntegrationEvent"/>
/// (compensa Tenant/Auth/Subscription si ya llegaron a existir) — ambos ANTES de
/// <c>unitOfWork.SaveChangesAsync</c>, mismo patrón que el resto del módulo Onboarding
/// (ver <c>OnboardingPaymentSucceededConsumer</c>). Con el transactional outbox de Wolverine
/// (<c>UseEntityFrameworkCoreTransactions</c> + <c>AutoApplyTransactions</c>), <c>PublishAsync</c>
/// encola el mensaje dentro de la MISMA transacción de EF que compromete <c>SaveChangesAsync</c> —
/// si se invirtiera el orden, el outbox insert quedaría en una transacción aparte y un crash entre
/// el commit del aggregate y el publish dejaría el onboarding "Refunded" en BD sin que ningún
/// consumer se enterara jamás (bug real F04 detectado en auditoría, corregido acá). También registra
/// <c>duration_seconds</c> (outcome "refunded") — auditoría F18, antes solo se medía en el happy path.</summary>
public static class CancelAndRefundOnboardingAdminHandler
{
    private const string RequiredConfirmation = "I understand this is irreversible";

    public static async Task<Result> Handle(
        CancelAndRefundOnboardingAdminCommand command,
        ITenantOnboardingRepository onboardings,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        IOnboardingMetrics metrics,
        IAuthAuditWriter audit,
        IRequestContext request,
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

        metrics.RecordDurationSeconds((DateTime.UtcNow - onboarding.CreatedAtUtc).TotalSeconds, "refunded");

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

        // Categoría M — la acción dispara un reembolso Stripe real, invariante §4 del plan de rate
        // limiting exige rastro de auditoría. onboarding.TenantId puede ser null (onboarding
        // cancelado antes de que el tenant llegara a existir) — se usa el sentinel PlatformTenant
        // (Guid.Empty) ya establecido para este mismo caso en el resto del módulo Onboarding.
        await audit.AddAsync(
            AuthAuditLog.Record(
                onboarding.TenantId ?? Guid.Empty,
                command.AdminUserId,
                AuthAuditAction.OnboardingAdminCancelledAndRefunded,
                true,
                request.IpAddress,
                request.UserAgent,
                correlation.CorrelationId,
                targetType: "TenantOnboarding",
                targetId: onboarding.Id
            ),
            ct
        );

        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }
}
