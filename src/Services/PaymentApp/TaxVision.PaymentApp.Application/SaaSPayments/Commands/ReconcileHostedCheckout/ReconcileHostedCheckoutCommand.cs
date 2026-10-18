using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Application.Abstractions.Payments;
using TaxVision.PaymentApp.Application.SaaSPayments.Common;
using TaxVision.PaymentApp.Domain.SaaSPayments;
using TaxVision.PaymentApp.Domain.ValueObjects;
using Wolverine;

namespace TaxVision.PaymentApp.Application.SaaSPayments.Commands.ReconcileHostedCheckout;

/// <summary>
/// Al volver del hosted checkout (PayPal/Stripe), el landing dispara esto para capturar/finalizar al
/// instante y no esperar al webhook. Mismo patrón que <c>ReconcileOnboardingCheckoutHandler</c>, pero
/// para los tipos post-onboarding (renewal, add-on, seats, plan change). PayPal es más notorio porque
/// requiere <c>Capture Order</c> explícito: sin esto el usuario ve el banner "pending" por minutos.
/// </summary>
public sealed record ReconcileHostedCheckoutCommand(Guid PaymentId, Guid TenantId);

public sealed record ReconcileHostedCheckoutResponse(
    Guid PaymentId,
    string Status,
    string Type,
    long AmountCents,
    string Currency,
    string? FailureCode,
    string? FailureMessage,
    string? ProviderPaymentReference,
    DateTime? PaidAtUtc
);

public static class ReconcileHostedCheckoutHandler
{
    // Solo los tipos que nacen de un hosted-checkout (redirect al provider). Un off-session o
    // Onboarding tienen rutas propias y no deben entrar acá: equivocarse de endpoint devuelve 400.
    private static readonly HashSet<SaaSPaymentType> ReconcilableTypes =
    [
        SaaSPaymentType.SubscriptionRenewalCheckout,
        SaaSPaymentType.AddOnPurchaseCharge,
        SaaSPaymentType.SeatsPurchaseCharge,
        SaaSPaymentType.PlanChangeCheckout,
    ];

    public static async Task<Result<ReconcileHostedCheckoutResponse>> Handle(
        ReconcileHostedCheckoutCommand command,
        ISaaSPaymentRepository payments,
        IPaymentAdapterFactory providerFactory,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        IMessageBus bus,
        ILogger<SaaSPayment> logger,
        CancellationToken ct,
        ITenantRegistry? tenants = null
    )
    {
        var payment = await payments.GetByIdAsync(command.PaymentId, command.TenantId, ct);
        if (payment is null)
            return Result.Failure<ReconcileHostedCheckoutResponse>(
                new Error("SaaSPayment.NotFound", "Payment not found.")
            );

        // Guard cross-tenant: GetByIdAsync ya filtra por tenant, pero verificamos explícito por si
        // una variante del repo relaja el filtro — nunca devolver un pago de otro tenant.
        if (payment.TenantId != command.TenantId)
            return Result.Failure<ReconcileHostedCheckoutResponse>(
                new Error("SaaSPayment.NotFound", "Payment not found.")
            );

        if (!ReconcilableTypes.Contains(payment.Type))
            return Result.Failure<ReconcileHostedCheckoutResponse>(
                new Error(
                    "SaaSPayment.NotReconcilable",
                    "This payment type does not support hosted-checkout reconcile."
                )
            );

        // Idempotente: si ya es terminal (webhook llegó antes) devolvemos el estado actual.
        if (IsTerminal(payment.Status))
            return Result.Success(BuildResponse(payment));

        if (payment.ExternalChargeReference is null)
            return Result.Failure<ReconcileHostedCheckoutResponse>(
                new Error("Checkout.NoSession", "This checkout has no provider reference.")
            );

        IPaymentProvider adapter;
        try
        {
            adapter = providerFactory.Resolve(payment.ProviderCode);
        }
        catch (InvalidOperationException)
        {
            return Result.Failure<ReconcileHostedCheckoutResponse>(
                new Error("PaymentProvider.NotConfigured", "The selected payment provider is not configured.")
            );
        }

        // Finalize = Capture Order (PayPal) o retrieve+confirm (Stripe). Equipara la latencia.
        var statusResult = await adapter.FinalizeHostedCheckoutAsync(
            payment.ExternalChargeReference.Value,
            payment.Amount,
            ct
        );
        if (statusResult.IsFailure)
            return Result.Failure<ReconcileHostedCheckoutResponse>(statusResult.Error);

        var nowUtc = DateTime.UtcNow;
        var outcome = statusResult.Value;
        ReconcileReferenceIfNeeded(payment, outcome.ProviderChargeReference, logger, nowUtc);

        var changed = await ApplyOutcomeAsync(payment, outcome, bus, correlation.CorrelationId, nowUtc, tenants, ct);
        if (changed)
            await unitOfWork.SaveChangesAsync(ct);

        return Result.Success(BuildResponse(payment));
    }

    private static async Task<bool> ApplyOutcomeAsync(
        SaaSPayment payment,
        ChargeAuthorizationResult outcome,
        IMessageBus bus,
        string correlationId,
        DateTime nowUtc,
        ITenantRegistry? tenants,
        CancellationToken ct
    )
    {
        switch (outcome.Status)
        {
            case PaymentStatus.Succeeded:
                var succeeded = payment.MarkSucceeded(nowUtc, Guid.Empty);
                if (succeeded.IsSuccess)
                    await SaaSPaymentResultPublisher.PublishAsync(payment, bus, correlationId, ct, tenants);
                return succeeded.IsSuccess;

            case PaymentStatus.Failed
            or PaymentStatus.Cancelled:
                var failed = payment.MarkFailed(
                    outcome.FailureCode ?? "Provider.Unknown",
                    outcome.FailureMessage ?? "The provider reported the charge as failed.",
                    willRetry: false,
                    nextRetryAtUtc: null,
                    Guid.Empty,
                    nowUtc
                );
                if (failed.IsSuccess)
                    await SaaSPaymentResultPublisher.PublishAsync(payment, bus, correlationId, ct, tenants);
                return failed.IsSuccess;

            default:
                return false;
        }
    }

    private static void ReconcileReferenceIfNeeded(
        SaaSPayment payment,
        string providerChargeReference,
        ILogger<SaaSPayment> logger,
        DateTime nowUtc
    )
    {
        if (
            string.IsNullOrWhiteSpace(providerChargeReference)
            || providerChargeReference == payment.ExternalChargeReference?.Value
        )
            return;

        var referenceResult = ExternalPaymentReference.Create(payment.ProviderCode, providerChargeReference);
        if (referenceResult.IsFailure)
        {
            logger.LogWarning(
                "Provider returned an invalid reconciled charge reference for SaaSPayment {SaaSPaymentId}: {ErrorCode}: {ErrorMessage}",
                payment.Id,
                referenceResult.Error.Code,
                referenceResult.Error.Message
            );
            return;
        }

        var reconcile = payment.ReconcileProviderChargeReference(referenceResult.Value, nowUtc);
        if (reconcile.IsFailure)
            logger.LogWarning(
                "Could not reconcile charge reference for SaaSPayment {SaaSPaymentId}: {ErrorCode}: {ErrorMessage}",
                payment.Id,
                reconcile.Error.Code,
                reconcile.Error.Message
            );
    }

    private static ReconcileHostedCheckoutResponse BuildResponse(SaaSPayment payment) =>
        new(
            payment.Id,
            payment.Status.ToString(),
            payment.Type.ToString(),
            payment.Amount.AmountCents,
            payment.Amount.Currency,
            payment.FailureCode,
            payment.FailureReason,
            payment.ExternalChargeReference?.Value,
            payment.PaidAtUtc
        );

    private static bool IsTerminal(PaymentStatus status) =>
        status
            is PaymentStatus.Succeeded
                or PaymentStatus.Failed
                or PaymentStatus.Cancelled
                or PaymentStatus.Refunded
                or PaymentStatus.PartiallyRefunded
                or PaymentStatus.ChargedBack;
}
