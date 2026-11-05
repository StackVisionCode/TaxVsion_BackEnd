namespace TaxVision.Connectors.Application.Sync;

/// <summary>Entrada de la re-sincronización (endpoint interno): valida y encola la primera página.</summary>
public sealed record StartMailboxResyncCommand(Guid TenantId, Guid AccountId, DateTime FromUtc, DateTime ToUtc);
