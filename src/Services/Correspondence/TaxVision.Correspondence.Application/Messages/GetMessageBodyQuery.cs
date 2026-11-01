namespace TaxVision.Correspondence.Application.Messages;

/// <summary>
/// <paramref name="VisibleAccountIds"/> null = ve todo; si no, el hilo del mensaje debe ser visible o NotFound (gate de
/// buzón de oficina). Abrir el cuerpo lo marca leído: <paramref name="ActorUserId"/> y <paramref name="CorrelationId"/>
/// viajan en el aviso en vivo.
/// </summary>
public sealed record GetMessageBodyQuery(
    Guid TenantId,
    Guid IncomingEmailId,
    IReadOnlyCollection<Guid>? VisibleAccountIds = null,
    Guid? ActorUserId = null,
    string CorrelationId = ""
);
