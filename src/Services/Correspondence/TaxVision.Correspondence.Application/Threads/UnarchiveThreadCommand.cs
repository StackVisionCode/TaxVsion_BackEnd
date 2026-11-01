namespace TaxVision.Correspondence.Application.Threads;

/// <summary>
/// Devuelve un hilo archivado al Inbox.
/// <para>
/// Gate: <paramref name="VisibleAccountIds"/> (buzón visible, <c>null</c> = todos) y <paramref name="RestrictToUserId"/>
/// (cliente asignado, <c>null</c> = todos). <paramref name="ActorUserId"/> y <paramref name="CorrelationId"/>
/// (del scope HTTP: el de Wolverine llega vacío) viajan en el aviso en vivo del cambio.
/// </para>
/// </summary>
public sealed record UnarchiveThreadCommand(
    Guid TenantId,
    Guid ThreadId,
    IReadOnlyCollection<Guid>? VisibleAccountIds = null,
    Guid? RestrictToUserId = null,
    Guid? ActorUserId = null,
    string CorrelationId = ""
);
