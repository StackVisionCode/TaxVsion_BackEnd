namespace BuildingBlocks.Messaging.PaymentClientIntegrationEvents;

/// <summary>
/// Publicado cuando un cobro de PaymentClient (TenantPayment de un link) llega a <c>Succeeded</c> —
/// tanto por el redeem síncrono como por confirmación async del webhook (3DS/SCA). El dueño del
/// recurso lo consume para reconciliar: para <c>PurposeKind = InvoicePayment</c>, Billing marca la
/// factura pagada. Semántica "pagado" (a diferencia de <c>PaymentLinkUsedIntegrationEvent</c>, que es
/// "link redimido"). Lleva la referencia externa (id de factura) + monto/moneda/proveedor para
/// correlacionar y validar; el consumer debe ser idempotente (dedup por factura ya pagada / EventId).
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
