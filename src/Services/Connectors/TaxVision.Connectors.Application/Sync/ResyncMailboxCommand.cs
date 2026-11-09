namespace TaxVision.Connectors.Application.Sync;

/// <summary>
/// Una página de la re-sincronización de un buzón (CON-07). Va a la cola local durable
/// <c>mailbox-resync</c>; cada página devuelve la siguiente, así que las de una cuenta van una tras otra.
/// </summary>
public sealed record ResyncMailboxCommand(
    Guid TenantId,
    Guid AccountId,
    DateTime FromUtc,
    DateTime ToUtc,
    string? PageToken,
    string CorrelationId = ""
);
