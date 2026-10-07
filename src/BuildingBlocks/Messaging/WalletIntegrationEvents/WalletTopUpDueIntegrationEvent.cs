namespace BuildingBlocks.Messaging.WalletIntegrationEvents;

/// <summary>
/// Intent publicado por <c>TaxVision.Wallet</c> cuando un tenant inicia una recarga: PaymentApp debe
/// cobrar <see cref="AmountCents"/> off-session (tarjeta guardada) y responder con
/// <c>WalletTopUpPaymentSucceeded/FailedIntegrationEvent</c>. Espejo de
/// <see cref="SubscriptionIntegrationEvents.SubscriptionPlanChangeDueIntegrationEvent"/>.
/// <para>El importe viaja en <b>centavos</b> (unidad nativa de PaymentApp/Stripe). Al confirmarse,
/// Wallet acredita <c>AmountCents * 10_000</c> micros. <see cref="IdempotencyKey"/> = clave de la orden
/// de recarga: un reintento continúa la misma operación, no genera un segundo cobro.</para>
/// </summary>
public sealed record WalletTopUpDueIntegrationEvent : IntegrationEvent
{
    /// <summary>Id de la orden de recarga (aggregate <c>WalletTopUp</c> en Wallet). Correlación estable.</summary>
    public required Guid TopUpId { get; init; }

    /// <summary>Monto a cobrar por Stripe, en centavos.</summary>
    public required long AmountCents { get; init; }
    public required string Currency { get; init; }

    public required string IdempotencyKey { get; init; }

    /// <summary>Usuario que pidió la recarga (para avisarle cuando su cargo se confirme).</summary>
    public required Guid RequestedByUserId { get; init; }
}
