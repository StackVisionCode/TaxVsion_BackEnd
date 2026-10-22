namespace TaxVision.Connectors.Application.Sync;

/// <summary>Revisar si un correo recibido es un aviso de no entrega. Va a una cola local durable.</summary>
public sealed record InspectDeliveryReport(
    Guid AccountId,
    Guid TenantId,
    string ProviderMessageId,
    DateTime ReceivedAtUtc,
    string CorrelationId
);
