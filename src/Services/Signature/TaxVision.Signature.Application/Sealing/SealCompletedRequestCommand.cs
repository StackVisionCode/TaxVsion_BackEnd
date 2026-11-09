using BuildingBlocks.Messaging.SignatureIntegrationEvents;

namespace TaxVision.Signature.Application.Sealing;

/// <summary>
/// Pide la fase A del sellado (SIG-6) de una solicitud completada. Es un comando local durable porque Wolverine
/// ejecuta en un solo handler todo lo que maneja el mismo mensaje: con el sellado en el evento, su
/// <c>[NonTransactional]</c> dejaba sin transacción a la auditoría, y sus reintentos la repetían.
/// </summary>
public sealed record SealCompletedRequestCommand(
    Guid TenantId,
    Guid SignatureRequestId,
    IReadOnlyList<Guid> DocumentIds,
    DateTime CompletedAtUtc,
    string? CorrelationId,
    Guid SourceEventId
);

/// <summary>Corre en la transacción de la línea <c>RequestCompleted</c>: el comando sale con ella por el outbox.</summary>
public static class CompletedRequestSealingConsumer
{
    public static SealCompletedRequestCommand Handle(SignatureRequestCompletedIntegrationEvent evt) =>
        new(
            evt.TenantId,
            evt.SignatureRequestId,
            evt.Documents.Select(document => document.DocumentId).ToList(),
            evt.CompletedAtUtc,
            evt.CorrelationId,
            evt.EventId
        );
}
