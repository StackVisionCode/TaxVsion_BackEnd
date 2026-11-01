namespace TaxVision.Correspondence.Application.Threads;

/// <summary>
/// Marca TODOS los correos inbound de un hilo como leídos/no-leídos de una vez ("mark all as read"
/// / "all as unread"), estado compartido por el tenant. Un solo comando con <see cref="IsRead"/>,
/// mismo criterio que <see cref="Messages.SetMessageReadStateCommand"/>.
/// <para>
/// Gate: <paramref name="VisibleAccountIds"/> (buzón visible, <c>null</c> = todos) y <paramref name="RestrictToUserId"/>
/// (cliente asignado, <c>null</c> = todos). <paramref name="ActorUserId"/> y <paramref name="CorrelationId"/>
/// (del scope HTTP: el de Wolverine llega vacío) viajan en el aviso en vivo del cambio.
/// </para>
/// </summary>
public sealed record SetThreadReadStateCommand(
    Guid TenantId,
    Guid ThreadId,
    bool IsRead,
    IReadOnlyCollection<Guid>? VisibleAccountIds = null,
    Guid? RestrictToUserId = null,
    Guid? ActorUserId = null,
    string CorrelationId = ""
);
