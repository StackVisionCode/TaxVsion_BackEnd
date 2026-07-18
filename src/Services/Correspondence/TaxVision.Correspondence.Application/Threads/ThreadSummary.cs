namespace TaxVision.Correspondence.Application.Threads;

/// <summary>
/// Fila de metadata para <c>GET /correspondence/customers/{customerId}/threads</c> (Fase 9).
/// A propósito no expone <c>ProviderThreadId</c> (detalle de threading interno de Connectors,
/// sin valor para el cliente final) ni <c>CustomerId</c> (ya viene en la ruta de la request).
/// </summary>
public sealed record ThreadSummary(
    Guid ThreadId,
    string Subject,
    string Status,
    int MessageCount,
    DateTime FirstMessageAtUtc,
    DateTime LastMessageAtUtc
);
