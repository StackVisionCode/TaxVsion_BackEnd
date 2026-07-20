namespace BuildingBlocks.Messaging.PaymentIntegrationEvents;

/// <summary>
/// Envelope financiero común publicado por PaymentApp o PaymentClient. Los IDs de
/// reserva/snapshot son referencias opacas; Payment continúa siendo el único owner
/// del estado financiero y Growth solo aplica políticas comerciales versionadas.
/// </summary>
public abstract record PaymentLifecycleIntegrationEvent : IntegrationEvent
{
    public abstract string EventType { get; }
    public int EventVersion { get; init; } = 1;
    public DateTime OccurredAt => OccurredOn;
    public string CausationId { get; init; } = string.Empty;
    public string TraceId { get; init; } = string.Empty;
    public required Guid AggregateId { get; init; }
    public required long AggregateVersion { get; init; }
    public required string PaymentSource { get; init; }
    public required Guid PaymentId { get; init; }
    public required long GrossAmountCents { get; init; }
    public required long DiscountAmountCents { get; init; }
    public required long NetAmountCents { get; init; }
    public required string Currency { get; init; }
    public Guid? CodeReservationId { get; init; }
    public Guid? ReferralAttributionId { get; init; }
    public Guid? PromotionSnapshotId { get; init; }
    public string? PromotionSnapshotHash { get; init; }
}

public sealed record PaymentSucceededIntegrationEvent : PaymentLifecycleIntegrationEvent
{
    public override string EventType => "payments.payment_succeeded";
    public required bool IsFirstSuccessfulPayment { get; init; }
    public required DateTime PaidAtUtc { get; init; }
}

public sealed record PaymentFailedIntegrationEvent : PaymentLifecycleIntegrationEvent
{
    public override string EventType => "payments.payment_failed";
    public required string FailureCode { get; init; }
    public required bool IsTerminal { get; init; }
    public required DateTime FailedAtUtc { get; init; }
}

public sealed record PaymentCancelledIntegrationEvent : PaymentLifecycleIntegrationEvent
{
    public override string EventType => "payments.payment_cancelled";
    public required string ReasonCode { get; init; }
    public required DateTime CancelledAtUtc { get; init; }
}

/// <summary>
/// RefundDeltaAmountCents representa este movimiento y
/// CumulativeRefundedAmountCents el total autoritativo del pago.
/// </summary>
public sealed record PaymentRefundedIntegrationEvent : PaymentLifecycleIntegrationEvent
{
    public override string EventType => "payments.payment_refunded";
    public required string RefundReference { get; init; }
    public required long RefundDeltaAmountCents { get; init; }
    public required long CumulativeRefundedAmountCents { get; init; }
    public required DateTime RefundedAtUtc { get; init; }
}

/// <summary>
/// DisputeVersion permite ignorar redeliveries y cambios fuera de orden sin
/// convertir a Growth en owner del chargeback.
/// </summary>
public sealed record PaymentChargebackChangedIntegrationEvent : PaymentLifecycleIntegrationEvent
{
    public override string EventType => "payments.chargeback_changed";
    public required string DisputeId { get; init; }
    public required string DisputeStatus { get; init; }
    public required long DisputeVersion { get; init; }
    public required long DisputedAmountCents { get; init; }
    public required DateTime ChangedAtUtc { get; init; }
}
