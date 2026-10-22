namespace TaxVision.Connectors.Application.Sync;

/// <summary>
/// Push de Gmail ya validado (JWT de Google) y encolado en una cola local durable. El webhook responde
/// apenas lo guarda: sincronizar dentro del request lo hacía lento y Pub/Sub reintentaba de más.
/// </summary>
/// <param name="CorrelationId">El de la request del webhook: la cola no hereda el contexto ambient.</param>
public sealed record GmailPushReceived(string EmailAddress, string HistoryId, string CorrelationId);
