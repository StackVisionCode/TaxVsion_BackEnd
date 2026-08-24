using BuildingBlocks.Results;
using TaxVision.Auth.Application.Onboarding.Abstractions;
using TaxVision.Auth.Application.Onboarding.TenantOnboardings.Commands;
using Wolverine;

namespace TaxVision.Auth.Application.Onboarding.Admin.Commands;

public sealed record ResendOnboardingReceiptAdminCommand(Guid OnboardingId);

/// <summary>Rescate manual del recibo de un onboarding ya pagado cuyo
/// <see cref="RequestOnboardingReceiptCommand"/> se agotó en reintentos (cola local en memoria: el
/// mensaje no queda en dead-letters). Reconstruye el comando desde el aggregate y lo re-publica.
/// Idempotente en Documents por Idempotency-Key, así que re-disparar no duplica el PDF.</summary>
public static class ResendOnboardingReceiptAdminHandler
{
    public static async Task<Result> Handle(
        ResendOnboardingReceiptAdminCommand command,
        ITenantOnboardingRepository onboardings,
        IPlanCatalogClient planCatalog,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var onboarding = await onboardings.GetByIdAsync(command.OnboardingId, ct);
        if (onboarding is null)
            return Result.Failure(new Error("Onboarding.NotFound", "Onboarding not found."));

        if (onboarding.PaymentCompletedAtUtc is null)
            return Result.Failure(
                new Error("Onboarding.NotPaid", "Onboarding has no settled payment; nothing to receipt.")
            );

        var planName = await planCatalog.GetPlanNameAsync(onboarding.PlanId, ct);
        var planLabel = string.IsNullOrWhiteSpace(planName) ? "Suscripción TaxProffice" : planName!;

        await bus.PublishAsync(
            new RequestOnboardingReceiptCommand(
                onboarding.Id,
                onboarding.FirstName,
                onboarding.LastName,
                onboarding.Email,
                planLabel,
                onboarding.NetAmountCents ?? onboarding.GrossAmountCents ?? 0,
                onboarding.Currency ?? "USD",
                onboarding.PaymentCompletedAtUtc.Value,
                onboarding.PaymentReference,
                // El método de pago enmascarado no se persiste en el onboarding (venía del evento de pago);
                // el recibo re-generado no lo muestra, lo demás queda idéntico.
                null,
                $"resend-receipt:{onboarding.Id:N}"
            )
        );

        return Result.Success();
    }
}
