using BuildingBlocks.Messaging.PaymentAppIntegrationEvents;
using BuildingBlocks.Messaging.PaymentIntegrationEvents;
using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Application.AddOnCheckouts;
using TaxVision.PaymentApp.Application.SaaSPayments.Commands.ProcessStripeWebhook;
using TaxVision.PaymentApp.Application.SeatsCheckouts;
using TaxVision.PaymentApp.Application.SubscriptionRenewalCheckouts;
using TaxVision.PaymentApp.Domain.SaaSPayments;
using TaxVision.PaymentApp.Domain.ValueObjects;
using Wolverine;

namespace TaxVision.PaymentApp.Application.SaaSPayments.Common;

/// <summary>
/// Único punto que publica el resultado de un <see cref="SaaSPayment"/> según su tipo. Lo usan el
/// cobro directo, el reintento, el webhook y la reconciliación, así un cobro que se resuelve tarde
/// (Processing, 3DS) notifica igual que uno resuelto al instante. No-op si el pago no es terminal.
/// </summary>
public static class SaaSPaymentResultPublisher
{
    public static async ValueTask PublishAsync(
        SaaSPayment payment,
        IMessageBus bus,
        string correlationId,
        CancellationToken ct,
        ITenantRegistry? tenants = null
    )
    {
        await PublishByTypeAsync(payment, bus, correlationId, ct);
        await PublishReceiptRequestAsync(payment, bus, correlationId, tenants, ct);
    }

    /// <summary>
    /// Todo cobro confirmado de un tenant real pide su recibo. Va aparte del evento por tipo porque no le
    /// dice a Subscription qué aprovisionar: solo dice que se cobró. El pago de un onboarding no entra —
    /// nace sin tenant y su recibo lo pide Auth cuando la saga termina.
    /// </summary>
    public static async ValueTask PublishReceiptRequestAsync(
        SaaSPayment payment,
        IMessageBus bus,
        string correlationId,
        ITenantRegistry? tenants,
        CancellationToken ct
    )
    {
        if (payment.Status != PaymentStatus.Succeeded || payment.TenantId == Guid.Empty)
            return;

        // Sin registro de tenants el recibo igual sale: el importe y el emisor son lo que lo hacen válido.
        var officeName = tenants is null ? null : (await tenants.GetByIdAsync(payment.TenantId, ct))?.Name;

        await bus.PublishAsync(
            new SaaSPaymentSucceededIntegrationEvent
            {
                TenantId = payment.TenantId,
                SaaSPaymentId = payment.Id,
                OfficeName = officeName ?? string.Empty,
                PaymentType = payment.Type.ToString(),
                AmountPaidCents = payment.Amount.AmountCents,
                Currency = payment.Amount.Currency,
                PaidAtUtc = payment.PaidAtUtc ?? DateTime.UtcNow,
                ProviderReferenceMask = Mask(payment.ExternalChargeReference?.Value),
                Quantity = payment.Breakdown?.Quantity,
                UnitAmountCents = payment.Breakdown?.UnitAmountCents,
                CorrelationId = correlationId,
            }
        );
    }

    /// <summary>Nunca la referencia completa: solo lo justo para reconocerla en el extracto.</summary>
    private static string Mask(string? reference) =>
        string.IsNullOrWhiteSpace(reference) ? string.Empty
        : reference.Length <= 4 ? reference
        : reference[^4..];

    private static ValueTask PublishByTypeAsync(
        SaaSPayment payment,
        IMessageBus bus,
        string correlationId,
        CancellationToken ct
    ) =>
        payment.Type switch
        {
            SaaSPaymentType.OnboardingInitial => ProcessStripeWebhookHandler.PublishOnboardingResultAsync(
                payment,
                bus,
                correlationId,
                ct
            ),
            SaaSPaymentType.SeatsPurchaseCharge => SeatsCheckoutResultPublisher.PublishAsync(
                payment,
                bus,
                correlationId,
                ct
            ),
            SaaSPaymentType.AddOnPurchaseCharge => AddOnCheckoutResultPublisher.PublishAsync(
                payment,
                bus,
                correlationId,
                ct
            ),
            SaaSPaymentType.SubscriptionRenewalCheckout => SubscriptionRenewalCheckoutResultPublisher.PublishAsync(
                payment,
                bus,
                correlationId,
                ct
            ),
            SaaSPaymentType.SubscriptionRenewal => PublishSubscriptionRenewalResultAsync(payment, bus, correlationId),
            SaaSPaymentType.SeatRenewal => PublishSeatRenewalResultAsync(payment, bus, correlationId),
            SaaSPaymentType.AddOnRenewal => PublishAddOnRenewalResultAsync(payment, bus, correlationId),
            // El checkout y el cobro off-session del upgrade cierran el MISMO PlanChangeRequest: mismo evento,
            // mismos consumers. Lo único que cambia es de dónde salió el dinero.
            SaaSPaymentType.PlanChangeCharge or SaaSPaymentType.PlanChangeCheckout => PublishPlanChangeResultAsync(
                payment,
                bus,
                correlationId
            ),
            _ => ValueTask.CompletedTask,
        };

    // Un cobro off-session cancelado por el provider nunca se va a cobrar: para Subscription es un fallo.
    private static bool IsFailure(SaaSPayment payment) =>
        payment.Status is PaymentStatus.Failed or PaymentStatus.Cancelled;

    private static string FailureCode(SaaSPayment payment) =>
        payment.FailureCode ?? (payment.Status == PaymentStatus.Cancelled ? "Provider.Cancelled" : "Unknown");

    private static string FailureReason(SaaSPayment payment) =>
        payment.FailureReason
        ?? (payment.Status == PaymentStatus.Cancelled ? "The payment was cancelled." : "The charge failed.");

    private static async ValueTask PublishSubscriptionRenewalResultAsync(
        SaaSPayment payment,
        IMessageBus bus,
        string correlationId
    )
    {
        if (IsFailure(payment))
        {
            await bus.PublishAsync(
                new SubscriptionRenewalPaymentFailedIntegrationEvent
                {
                    TenantId = payment.TenantId,
                    TenantSubscriptionId = payment.TargetAggregateId,
                    SaaSPaymentId = payment.Id,
                    IdempotencyKey = payment.IdempotencyKey.Value,
                    FailureCode = FailureCode(payment),
                    FailureReason = FailureReason(payment),
                    WillRetry = payment.NextRetryAtUtc is not null,
                    NextRetryAtUtc = payment.NextRetryAtUtc,
                    CorrelationId = correlationId,
                }
            );
            return;
        }

        if (payment.Status != PaymentStatus.Succeeded)
            return;

        await bus.PublishAsync(
            new SubscriptionRenewalPaymentSucceededIntegrationEvent
            {
                TenantId = payment.TenantId,
                TenantSubscriptionId = payment.TargetAggregateId,
                SaaSPaymentId = payment.Id,
                IdempotencyKey = payment.IdempotencyKey.Value,
                ExternalPaymentReference = payment.ExternalChargeReference?.Value ?? string.Empty,
                PaidAtUtc = payment.PaidAtUtc ?? DateTime.UtcNow,
                CorrelationId = correlationId,
            }
        );

        // El descuento de bienvenida del referido se reservó en Growth al activar la suscripción:
        // se confirma con el envelope financiero genérico que Growth ya consume.
        if (payment.CodeReservationId is { } reservationId && payment.CodeReservationPaymentId is { } paymentId)
        {
            await bus.PublishAsync(
                new PaymentSucceededIntegrationEvent
                {
                    TenantId = payment.TenantId,
                    AggregateId = payment.TargetAggregateId,
                    AggregateVersion = 1,
                    PaymentSource = "PaymentApp",
                    PaymentId = paymentId,
                    GrossAmountCents = payment.Amount.AmountCents + (payment.DiscountAmountCents ?? 0),
                    DiscountAmountCents = payment.DiscountAmountCents ?? 0,
                    NetAmountCents = payment.Amount.AmountCents,
                    Currency = payment.Amount.Currency,
                    CodeReservationId = reservationId,
                    PromotionSnapshotHash = payment.PromotionSnapshotHash,
                    IsFirstSuccessfulPayment = true,
                    PaidAtUtc = payment.PaidAtUtc ?? DateTime.UtcNow,
                    CorrelationId = correlationId,
                }
            );
        }
    }

    private static ValueTask PublishSeatRenewalResultAsync(SaaSPayment payment, IMessageBus bus, string correlationId)
    {
        if (IsFailure(payment))
            return bus.PublishAsync(
                new SeatRenewalPaymentFailedIntegrationEvent
                {
                    TenantId = payment.TenantId,
                    SeatId = payment.TargetAggregateId,
                    SaaSPaymentId = payment.Id,
                    IdempotencyKey = payment.IdempotencyKey.Value,
                    FailureCode = FailureCode(payment),
                    FailureReason = FailureReason(payment),
                    WillRetry = payment.NextRetryAtUtc is not null,
                    NextRetryAtUtc = payment.NextRetryAtUtc,
                    CorrelationId = correlationId,
                }
            );

        if (payment.Status != PaymentStatus.Succeeded)
            return ValueTask.CompletedTask;

        return bus.PublishAsync(
            new SeatRenewalPaymentSucceededIntegrationEvent
            {
                TenantId = payment.TenantId,
                SeatId = payment.TargetAggregateId,
                SaaSPaymentId = payment.Id,
                IdempotencyKey = payment.IdempotencyKey.Value,
                ExternalPaymentReference = payment.ExternalChargeReference?.Value ?? string.Empty,
                PaidAtUtc = payment.PaidAtUtc ?? DateTime.UtcNow,
                CorrelationId = correlationId,
            }
        );
    }

    private static ValueTask PublishAddOnRenewalResultAsync(SaaSPayment payment, IMessageBus bus, string correlationId)
    {
        if (IsFailure(payment))
            return bus.PublishAsync(
                new AddOnRenewalPaymentFailedIntegrationEvent
                {
                    TenantId = payment.TenantId,
                    TenantAddOnId = payment.TargetAggregateId,
                    SaaSPaymentId = payment.Id,
                    IdempotencyKey = payment.IdempotencyKey.Value,
                    FailureCode = FailureCode(payment),
                    FailureReason = FailureReason(payment),
                    WillRetry = payment.NextRetryAtUtc is not null,
                    NextRetryAtUtc = payment.NextRetryAtUtc,
                    CorrelationId = correlationId,
                }
            );

        if (payment.Status != PaymentStatus.Succeeded)
            return ValueTask.CompletedTask;

        return bus.PublishAsync(
            new AddOnRenewalPaymentSucceededIntegrationEvent
            {
                TenantId = payment.TenantId,
                TenantAddOnId = payment.TargetAggregateId,
                SaaSPaymentId = payment.Id,
                IdempotencyKey = payment.IdempotencyKey.Value,
                ExternalPaymentReference = payment.ExternalChargeReference?.Value ?? string.Empty,
                PaidAtUtc = payment.PaidAtUtc ?? DateTime.UtcNow,
                CorrelationId = correlationId,
            }
        );
    }

    // TargetAggregateId es el PlanChangeRequestId: Subscription ubica el request sin campos extra.
    private static ValueTask PublishPlanChangeResultAsync(SaaSPayment payment, IMessageBus bus, string correlationId)
    {
        if (IsFailure(payment))
            return bus.PublishAsync(
                new SubscriptionPlanChangePaymentFailedIntegrationEvent
                {
                    TenantId = payment.TenantId,
                    PlanChangeRequestId = payment.TargetAggregateId,
                    SaaSPaymentId = payment.Id,
                    IdempotencyKey = payment.IdempotencyKey.Value,
                    FailureCode = FailureCode(payment),
                    FailureReason = FailureReason(payment),
                    RequestedByUserId = payment.CreatedBy,
                    CorrelationId = correlationId,
                }
            );

        if (payment.Status != PaymentStatus.Succeeded)
            return ValueTask.CompletedTask;

        return bus.PublishAsync(
            new SubscriptionPlanChangePaymentSucceededIntegrationEvent
            {
                TenantId = payment.TenantId,
                PlanChangeRequestId = payment.TargetAggregateId,
                SaaSPaymentId = payment.Id,
                IdempotencyKey = payment.IdempotencyKey.Value,
                ExternalPaymentReference = payment.ExternalChargeReference?.Value ?? string.Empty,
                PaidAtUtc = payment.PaidAtUtc ?? DateTime.UtcNow,
                RequestedByUserId = payment.CreatedBy,
                CorrelationId = correlationId,
            }
        );
    }
}
