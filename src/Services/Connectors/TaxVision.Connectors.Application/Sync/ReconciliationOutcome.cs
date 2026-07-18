namespace TaxVision.Connectors.Application.Sync;

/// <summary>
/// Resultado observable de un pase de reconciliación para UNA cuenta. ReconciliationJob (Infra)
/// decide a partir de esto qué loggear/medir — Application no puede depender de ConnectorsMetrics
/// (vive en Infrastructure, ver ConnectorsArchitectureTests.Application_should_not_depend_on_infrastructure).
/// </summary>
/// <param name="MessagesFound">Mensajes nuevos encontrados y publicados en este pase.</param>
/// <param name="CursorWasSeeded">
/// True si este pase creó el ProviderSyncCursor por primera vez (primer sync de la cuenta — el
/// caso normal para IMAP, que nunca tiene push). Un <see cref="MessagesFound"/> &gt; 0 con esto en
/// true es catch-up inicial esperado, NO evidencia de que un push se haya perdido.
/// </param>
/// <param name="Skipped">
/// True si el pase se saltó porque un sync disparado por webhook ya estaba en vuelo para esta
/// cuenta (mismo lock "connectors:webhook-sync:{accountId}" que los webhook handlers) — no se
/// tocó cursor ni se publicó nada, el sync en vuelo ya cubre lo que este pase hubiera hecho.
/// </param>
public sealed record ReconciliationOutcome(int MessagesFound, bool CursorWasSeeded, bool Skipped);
