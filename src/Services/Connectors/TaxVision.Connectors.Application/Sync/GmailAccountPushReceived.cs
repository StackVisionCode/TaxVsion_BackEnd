namespace TaxVision.Connectors.Application.Sync;

/// <summary>Un push de Gmail ya resuelto a una cuenta: cada cuenta sincroniza con su cursor y su lock.</summary>
/// <param name="CorrelationId">El de la request del webhook: la cola no hereda el contexto ambient.</param>
public sealed record GmailAccountPushReceived(Guid AccountId, string HistoryId, string CorrelationId);
