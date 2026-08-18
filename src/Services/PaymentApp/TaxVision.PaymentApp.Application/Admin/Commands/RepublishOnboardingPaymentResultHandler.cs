using BuildingBlocks.Common;
using BuildingBlocks.Results;
using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Application.SaaSPayments.Commands.ProcessStripeWebhook;
using TaxVision.PaymentApp.Domain.SaaSPayments;
using Wolverine;

namespace TaxVision.PaymentApp.Application.Admin.Commands;

/// <summary>
/// Reparo operacional para el gap encontrado en la verificación E2E de PayFlow: si
/// <c>PendingChargeReconciliationJob</c> resolvía un pago de onboarding a Succeeded/Failed
/// ANTES de que el fix que le agregó la publicación del evento existiera (o ANTES de que el
/// webhook lograra procesarse), el Saga de Auth (<c>TenantOnboardingProcessManager</c>) se
/// queda esperando un evento que nunca llegó -- el pago local ya está en su estado terminal
/// correcto, solo falta reenviar la notificación downstream. Reenviar el webhook original no
/// sirve: <c>ProcessStripeWebhookHandler.ApplyPayload</c> rechaza la transición porque el pago
/// ya no está en Processing/RequiresAction (queda "stale", sin publicar nada).
/// </summary>
public static class RepublishOnboardingPaymentResultHandler
{
    public static async Task<Result> Handle(
        RepublishOnboardingPaymentResultCommand command,
        ISaaSPaymentRepository payments,
        IMessageBus bus,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var payment = await payments.GetByIdAsync(command.SaaSPaymentId, ct);
        if (payment is null)
            return Result.Failure(new Error("SaaSPayment.NotFound", "No SaaSPayment exists with this id."));

        if (payment.Type != SaaSPaymentType.OnboardingInitial)
            return Result.Failure(
                new Error(
                    "SaaSPayment.NotOnboardingInitial",
                    "Only OnboardingInitial payments publish an onboarding result event."
                )
            );

        if (payment.Status is not (PaymentStatus.Succeeded or PaymentStatus.Failed))
            return Result.Failure(
                new Error(
                    "SaaSPayment.NotTerminal",
                    $"Payment must be Succeeded or Failed to republish its onboarding result; current status is {payment.Status}."
                )
            );

        await ProcessStripeWebhookHandler.PublishOnboardingResultAsync(payment, bus, correlation.CorrelationId, ct);
        return Result.Success();
    }
}
