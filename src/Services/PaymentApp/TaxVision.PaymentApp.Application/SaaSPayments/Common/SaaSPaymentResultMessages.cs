using BuildingBlocks.Messaging.PaymentAppIntegrationEvents;
using BuildingBlocks.Messaging.PaymentIntegrationEvents;
using BuildingBlocks.Tenancy;
using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Application.AddOnCheckouts;
using TaxVision.PaymentApp.Application.SeatsCheckouts;
using TaxVision.PaymentApp.Application.SubscriptionRenewalCheckouts;
using TaxVision.PaymentApp.Domain.SaaSPayments;
using TaxVision.PaymentApp.Domain.SaaSPayments.Events;
using TaxVision.PaymentApp.Domain.Tenants;

namespace TaxVision.PaymentApp.Application.SaaSPayments.Common;

/// <summary>
/// Avisos de integración del resultado de un <see cref="SaaSPayment"/> según su tipo. Los devuelve
/// <c>SaaSPaymentDomainEventHandler</c> con la foto del pago en la transición: así un cobro que se resuelve tarde
/// (Processing, 3DS) avisa igual que uno resuelto al instante. Vacío si el pago no es terminal.
/// </summary>
public static class SaaSPaymentResultMessages
{
    public static async Task<IReadOnlyList<object>> BuildAsync(
        SaaSPaymentSnapshot payment,
        string correlationId,
        CancellationToken ct,
        ITenantRegistry? tenants = null,
        ITenantProviderCustomerRepository? customers = null
    )
    {
        var messages = new List<object>();
        await AddByTypeAsync(messages, payment, correlationId, customers, ct);
        if (await ReceiptRequestAsync(payment, correlationId, tenants, ct) is { } receipt)
            messages.Add(receipt);
        return messages;
    }

    /// <summary>
    /// Todo cobro confirmado de un tenant real pide su recibo. Va aparte del evento por tipo porque no le
    /// dice a Subscription qué aprovisionar: solo dice que se cobró. El pago de un onboarding no entra —
    /// nace sin tenant y su recibo lo pide Auth cuando la saga termina.
    /// </summary>
    public static async Task<SaaSPaymentSucceededIntegrationEvent?> ReceiptRequestAsync(
        SaaSPaymentSnapshot payment,
        string correlationId,
        ITenantRegistry? tenants,
        CancellationToken ct
    )
    {
        if (payment.Status != PaymentStatus.Succeeded || payment.TenantId == Guid.Empty)
            return null;

        // Sin registro de tenants el recibo igual sale: el importe y el emisor son lo que lo hacen válido.
        var tenant = tenants is null ? null : await tenants.GetByIdAsync(payment.TenantId, ct);

        return new SaaSPaymentSucceededIntegrationEvent
        {
            TenantId = payment.TenantId,
            SaaSPaymentId = payment.SaaSPaymentId,
            OfficeName = tenant?.Name ?? string.Empty,
            BilledTo = BilledToOf(tenant),
            PaymentType = payment.Type.ToString(),
            AmountPaidCents = payment.AmountCents,
            Currency = payment.Currency,
            PaidAtUtc = payment.PaidAtUtc ?? DateTime.UtcNow,
            ProviderReferenceMask = Mask(payment.ExternalChargeReference),
            Quantity = payment.Quantity,
            UnitAmountCents = payment.UnitAmountCents,
            ConceptCode = payment.ConceptCode,
            ConceptDisplayName = payment.ConceptDisplayName,
            Lines = payment.Lines is null
                ? null
                :
                [
                    .. payment.Lines.Select(line => new SaaSChargeLine(
                        line.Description,
                        line.Quantity,
                        line.UnitAmountCents
                    )),
                ],
            CorrelationId = correlationId,
        };
    }

    /// <summary>Resultado del pago inicial de un onboarding (sin tenant todavía: viaja con el de plataforma).</summary>
    public static object? OnboardingResultOf(SaaSPaymentSnapshot payment, string correlationId)
    {
        if (payment.Status == PaymentStatus.Succeeded)
            return new OnboardingPaymentSucceededIntegrationEvent
            {
                TenantId = PlatformTenant.Id,
                OnboardingId = payment.OnboardingId!.Value,
                SaaSPaymentId = payment.SaaSPaymentId,
                PlanId = payment.TargetAggregateId,
                AmountPaidCents = payment.AmountCents,
                Currency = payment.Currency,
                PaidAtUtc = payment.PaidAtUtc ?? DateTime.UtcNow,
                ProviderPaymentReference = payment.ExternalChargeReference ?? string.Empty,
                CorrelationId = correlationId,
            };

        if (payment.Status is not (PaymentStatus.Failed or PaymentStatus.Cancelled))
            return null;

        // Para un onboarding, un cargo cancelado (ej. la sesión de checkout expiró y el proveedor
        // anuló el intento) es tan terminal como uno declinado: el comprador no pagó. Se notifica igual.
        var cancelled = payment.Status == PaymentStatus.Cancelled;
        return new OnboardingPaymentFailedIntegrationEvent
        {
            TenantId = PlatformTenant.Id,
            OnboardingId = payment.OnboardingId!.Value,
            SaaSPaymentId = payment.SaaSPaymentId,
            FailureCode = payment.FailureCode ?? (cancelled ? "Provider.Cancelled" : "Unknown"),
            FailureReason = payment.FailureReason ?? (cancelled ? "The payment was cancelled." : "The charge failed."),
            CorrelationId = correlationId,
        };
    }

    // Sin ningún dato del perfil viaja null y el recibo muestra solo el nombre de la oficina.
    private static SaaSBilledToData? BilledToOf(Tenant? tenant) =>
        tenant is null
        || (tenant.BillingLegalName is null && tenant.BillingEin is null && tenant.BillingAddress is null)
            ? null
            : new SaaSBilledToData(tenant.BillingLegalName, tenant.BillingEin, tenant.BillingAddress);

    /// <summary>Nunca la referencia completa: solo lo justo para reconocerla en el extracto.</summary>
    private static string Mask(string? reference) =>
        string.IsNullOrWhiteSpace(reference) ? string.Empty
        : reference.Length <= 4 ? reference
        : reference[^4..];

    private static async Task AddByTypeAsync(
        List<object> messages,
        SaaSPaymentSnapshot payment,
        string correlationId,
        ITenantProviderCustomerRepository? customers,
        CancellationToken ct
    )
    {
        switch (payment.Type)
        {
            case SaaSPaymentType.OnboardingInitial:
                AddIfAny(messages, OnboardingResultOf(payment, correlationId));
                break;
            case SaaSPaymentType.SeatsPurchaseCharge:
                AddIfAny(messages, SeatsCheckoutResultMessages.ResultOf(payment, correlationId));
                break;
            case SaaSPaymentType.AddOnPurchaseCharge:
                AddIfAny(messages, AddOnCheckoutResultMessages.ResultOf(payment, correlationId));
                break;
            case SaaSPaymentType.SubscriptionRenewalCheckout:
                AddIfAny(messages, SubscriptionRenewalCheckoutResultMessages.ResultOf(payment, correlationId));
                break;
            case SaaSPaymentType.SubscriptionRenewal:
                await AddSubscriptionRenewalResultAsync(messages, payment, correlationId, customers, ct);
                break;
            case SaaSPaymentType.SeatRenewal:
                AddIfAny(messages, await SeatRenewalResultAsync(payment, correlationId, customers, ct));
                break;
            case SaaSPaymentType.AddOnRenewal:
                AddIfAny(messages, await AddOnRenewalResultAsync(payment, correlationId, customers, ct));
                break;
            // El checkout y el cobro off-session del upgrade cierran el MISMO PlanChangeRequest: mismo evento,
            // mismos consumers. Lo único que cambia es de dónde salió el dinero.
            case SaaSPaymentType.PlanChangeCharge or SaaSPaymentType.PlanChangeCheckout:
                AddIfAny(messages, PlanChangeResultOf(payment, correlationId));
                break;
            case SaaSPaymentType.WalletTopUp:
                AddIfAny(messages, WalletTopUpResultOf(payment, correlationId));
                break;
        }
    }

    private static void AddIfAny(List<object> messages, object? message)
    {
        if (message is not null)
            messages.Add(message);
    }

    // Un cobro off-session cancelado por el provider nunca se va a cobrar: para Subscription es un fallo.
    private static bool IsFailure(SaaSPaymentSnapshot payment) =>
        payment.Status is PaymentStatus.Failed or PaymentStatus.Cancelled;

    private static string FailureCode(SaaSPaymentSnapshot payment) =>
        payment.FailureCode ?? (payment.Status == PaymentStatus.Cancelled ? "Provider.Cancelled" : "Unknown");

    private static string FailureReason(SaaSPaymentSnapshot payment) =>
        payment.FailureReason
        ?? (payment.Status == PaymentStatus.Cancelled ? "The payment was cancelled." : "The charge failed.");

    private static async Task AddSubscriptionRenewalResultAsync(
        List<object> messages,
        SaaSPaymentSnapshot payment,
        string correlationId,
        ITenantProviderCustomerRepository? customers,
        CancellationToken ct
    )
    {
        if (IsFailure(payment))
        {
            var facts = await ChargeAttemptFacts.ResolveAsync(payment, customers, ct);
            messages.Add(
                new SubscriptionRenewalPaymentFailedIntegrationEvent
                {
                    TenantId = payment.TenantId,
                    TenantSubscriptionId = payment.TargetAggregateId,
                    SaaSPaymentId = payment.SaaSPaymentId,
                    IdempotencyKey = payment.IdempotencyKey,
                    FailureCode = FailureCode(payment),
                    FailureReason = FailureReason(payment),
                    WillRetry = payment.NextRetryAtUtc is not null,
                    NextRetryAtUtc = payment.NextRetryAtUtc,
                    AttemptNumber = facts.AttemptNumber,
                    MaxAttempts = facts.MaxAttempts,
                    AmountCents = facts.AmountCents,
                    Currency = facts.Currency,
                    CardBrand = facts.CardBrand,
                    CardLast4 = facts.CardLast4,
                    IsAutomaticCharge = facts.IsAutomaticCharge,
                    CorrelationId = correlationId,
                }
            );
            return;
        }

        if (payment.Status != PaymentStatus.Succeeded)
            return;

        messages.Add(
            new SubscriptionRenewalPaymentSucceededIntegrationEvent
            {
                TenantId = payment.TenantId,
                TenantSubscriptionId = payment.TargetAggregateId,
                SaaSPaymentId = payment.SaaSPaymentId,
                IdempotencyKey = payment.IdempotencyKey,
                ExternalPaymentReference = payment.ExternalChargeReference ?? string.Empty,
                PaidAtUtc = payment.PaidAtUtc ?? DateTime.UtcNow,
                CorrelationId = correlationId,
            }
        );

        // El descuento de bienvenida del referido se reservó en Growth al activar la suscripción:
        // se confirma con el envelope financiero genérico que Growth ya consume.
        if (payment.CodeReservationId is { } reservationId && payment.CodeReservationPaymentId is { } paymentId)
        {
            messages.Add(
                new PaymentSucceededIntegrationEvent
                {
                    TenantId = payment.TenantId,
                    AggregateId = payment.TargetAggregateId,
                    AggregateVersion = 1,
                    PaymentSource = "PaymentApp",
                    PaymentId = paymentId,
                    GrossAmountCents = payment.AmountCents + (payment.DiscountAmountCents ?? 0),
                    DiscountAmountCents = payment.DiscountAmountCents ?? 0,
                    NetAmountCents = payment.AmountCents,
                    Currency = payment.Currency,
                    CodeReservationId = reservationId,
                    PromotionSnapshotHash = payment.PromotionSnapshotHash,
                    IsFirstSuccessfulPayment = true,
                    PaidAtUtc = payment.PaidAtUtc ?? DateTime.UtcNow,
                    CorrelationId = correlationId,
                }
            );
        }
    }

    private static async Task<object?> SeatRenewalResultAsync(
        SaaSPaymentSnapshot payment,
        string correlationId,
        ITenantProviderCustomerRepository? customers,
        CancellationToken ct
    )
    {
        if (IsFailure(payment))
        {
            var facts = await ChargeAttemptFacts.ResolveAsync(payment, customers, ct);
            return new SeatRenewalPaymentFailedIntegrationEvent
            {
                TenantId = payment.TenantId,
                SeatId = payment.TargetAggregateId,
                SaaSPaymentId = payment.SaaSPaymentId,
                IdempotencyKey = payment.IdempotencyKey,
                FailureCode = FailureCode(payment),
                FailureReason = FailureReason(payment),
                WillRetry = payment.NextRetryAtUtc is not null,
                NextRetryAtUtc = payment.NextRetryAtUtc,
                AttemptNumber = facts.AttemptNumber,
                MaxAttempts = facts.MaxAttempts,
                AmountCents = facts.AmountCents,
                Currency = facts.Currency,
                CardBrand = facts.CardBrand,
                CardLast4 = facts.CardLast4,
                IsAutomaticCharge = facts.IsAutomaticCharge,
                CorrelationId = correlationId,
            };
        }

        if (payment.Status != PaymentStatus.Succeeded)
            return null;

        return new SeatRenewalPaymentSucceededIntegrationEvent
        {
            TenantId = payment.TenantId,
            SeatId = payment.TargetAggregateId,
            SaaSPaymentId = payment.SaaSPaymentId,
            IdempotencyKey = payment.IdempotencyKey,
            ExternalPaymentReference = payment.ExternalChargeReference ?? string.Empty,
            PaidAtUtc = payment.PaidAtUtc ?? DateTime.UtcNow,
            CorrelationId = correlationId,
        };
    }

    private static async Task<object?> AddOnRenewalResultAsync(
        SaaSPaymentSnapshot payment,
        string correlationId,
        ITenantProviderCustomerRepository? customers,
        CancellationToken ct
    )
    {
        if (IsFailure(payment))
        {
            var facts = await ChargeAttemptFacts.ResolveAsync(payment, customers, ct);
            return new AddOnRenewalPaymentFailedIntegrationEvent
            {
                TenantId = payment.TenantId,
                TenantAddOnId = payment.TargetAggregateId,
                SaaSPaymentId = payment.SaaSPaymentId,
                IdempotencyKey = payment.IdempotencyKey,
                FailureCode = FailureCode(payment),
                FailureReason = FailureReason(payment),
                WillRetry = payment.NextRetryAtUtc is not null,
                NextRetryAtUtc = payment.NextRetryAtUtc,
                AttemptNumber = facts.AttemptNumber,
                MaxAttempts = facts.MaxAttempts,
                AmountCents = facts.AmountCents,
                Currency = facts.Currency,
                CardBrand = facts.CardBrand,
                CardLast4 = facts.CardLast4,
                IsAutomaticCharge = facts.IsAutomaticCharge,
                CorrelationId = correlationId,
            };
        }

        if (payment.Status != PaymentStatus.Succeeded)
            return null;

        return new AddOnRenewalPaymentSucceededIntegrationEvent
        {
            TenantId = payment.TenantId,
            TenantAddOnId = payment.TargetAggregateId,
            SaaSPaymentId = payment.SaaSPaymentId,
            IdempotencyKey = payment.IdempotencyKey,
            ExternalPaymentReference = payment.ExternalChargeReference ?? string.Empty,
            PaidAtUtc = payment.PaidAtUtc ?? DateTime.UtcNow,
            CorrelationId = correlationId,
        };
    }

    // TargetAggregateId es el PlanChangeRequestId: Subscription ubica el request sin campos extra.
    private static object? PlanChangeResultOf(SaaSPaymentSnapshot payment, string correlationId)
    {
        if (IsFailure(payment))
            return new SubscriptionPlanChangePaymentFailedIntegrationEvent
            {
                TenantId = payment.TenantId,
                PlanChangeRequestId = payment.TargetAggregateId,
                SaaSPaymentId = payment.SaaSPaymentId,
                IdempotencyKey = payment.IdempotencyKey,
                FailureCode = FailureCode(payment),
                FailureReason = FailureReason(payment),
                RequestedByUserId = payment.CreatedBy,
                CorrelationId = correlationId,
            };

        if (payment.Status != PaymentStatus.Succeeded)
            return null;

        return new SubscriptionPlanChangePaymentSucceededIntegrationEvent
        {
            TenantId = payment.TenantId,
            PlanChangeRequestId = payment.TargetAggregateId,
            SaaSPaymentId = payment.SaaSPaymentId,
            IdempotencyKey = payment.IdempotencyKey,
            ExternalPaymentReference = payment.ExternalChargeReference ?? string.Empty,
            PaidAtUtc = payment.PaidAtUtc ?? DateTime.UtcNow,
            RequestedByUserId = payment.CreatedBy,
            CorrelationId = correlationId,
        };
    }

    // Recarga de monedero (TaxVision.Wallet). TargetAggregateId = TopUpId; CreatedBy = quien recargó.
    private static object? WalletTopUpResultOf(SaaSPaymentSnapshot payment, string correlationId)
    {
        if (IsFailure(payment))
            return new WalletTopUpPaymentFailedIntegrationEvent
            {
                TenantId = payment.TenantId,
                TopUpId = payment.TargetAggregateId,
                SaaSPaymentId = payment.SaaSPaymentId,
                IdempotencyKey = payment.IdempotencyKey,
                Reason = FailureReason(payment),
                RequestedByUserId = payment.CreatedBy,
                CorrelationId = correlationId,
            };

        if (payment.Status != PaymentStatus.Succeeded)
            return null;

        return new WalletTopUpPaymentSucceededIntegrationEvent
        {
            TenantId = payment.TenantId,
            TopUpId = payment.TargetAggregateId,
            SaaSPaymentId = payment.SaaSPaymentId,
            IdempotencyKey = payment.IdempotencyKey,
            ExternalPaymentReference = payment.ExternalChargeReference ?? string.Empty,
            AmountCents = payment.AmountCents,
            Currency = payment.Currency,
            PaidAtUtc = payment.PaidAtUtc ?? DateTime.UtcNow,
            RequestedByUserId = payment.CreatedBy,
            CorrelationId = correlationId,
        };
    }
}
