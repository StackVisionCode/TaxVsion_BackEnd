namespace BuildingBlocks.Messaging.DocumentsIntegrationEvents;

/// <summary>
/// El recibo de un cobro SaaS ya está guardado y se puede descargar. PaymentApp lo consume para colgar el
/// <c>FileId</c> del pago, que es lo que el historial del Account necesita para ofrecer la descarga.
/// Alias v1.
/// </summary>
public sealed record SaaSReceiptReadyIntegrationEvent : IntegrationEvent
{
    public required Guid SaaSPaymentId { get; init; }
    public required Guid ReceiptFileId { get; init; }
}
