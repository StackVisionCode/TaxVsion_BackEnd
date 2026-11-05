namespace BuildingBlocks.Messaging.PaymentClientIntegrationEvents;

/// <summary>
/// Publicado cuando cualquier <c>TenantPayment</c> de PaymentClient llega a <c>Succeeded</c>, con link o
/// sin él: canje síncrono, webhook (3DS/SCA), reconciliación, cobro directo o recurrente. También puede
/// republicarse por reposición (con <c>EventId</c> nuevo). El dueño del recurso lo consume para
/// reconciliar: para <c>PurposeKind = InvoicePayment</c>, Billing aplica el pago a la factura. Semántica
/// "pagado" (a diferencia de <c>PaymentLinkUsedIntegrationEvent</c>, que es "link redimido"). Lleva la
/// referencia externa (id de factura) + monto/moneda/proveedor para correlacionar y validar; el
/// consumidor debe ser idempotente por <c>TenantPaymentId</c>.
/// </summary>
public sealed record TenantPaymentSucceededIntegrationEvent : IntegrationEvent
{
    public required Guid TenantPaymentId { get; init; }
    public required string ProviderCode { get; init; }
    public required string PurposeKind { get; init; }

    /// <summary>Recurso externo del tenant que originó el cobro (para InvoicePayment, el id de factura).</summary>
    public string? ExternalReferenceId { get; init; }
    public required long AmountCents { get; init; }
    public required string Currency { get; init; }
    public required DateTime PaidAtUtc { get; init; }
}
