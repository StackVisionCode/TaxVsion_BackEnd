namespace TaxVision.Connectors.Application.Sync;

/// <summary>
/// Reconciliación (safety net Gmail/Graph detrás del push, único mecanismo de sync para IMAP) —
/// dispara el MISMO RawMessageSyncOrchestrator que los webhook handlers, re-invocado manualmente
/// por ReconciliationJob (Infra) en vez de por un push/notification entrante. Ver
/// ReconcileAccountHandler.
/// </summary>
public sealed record ReconcileAccountCommand(Guid AccountId);
