namespace TaxVision.Correspondence.Application.Threads;

/// <summary>
/// Archiva un hilo.
/// <para>
/// Gate: <paramref name="VisibleAccountIds"/> (buzón visible, <c>null</c> = todos) y <paramref name="RestrictToUserId"/>
/// (cliente asignado, <c>null</c> = todos). <paramref name="ActorUserId"/> y <paramref name="CorrelationId"/>
/// (del scope HTTP: el de Wolverine llega vacío) viajan en el aviso en vivo del cambio.
/// </para>
/// </summary>
public sealed record ArchiveThreadCommand(
    Guid TenantId,
    Guid ThreadId,
    IReadOnlyCollection<Guid>? VisibleAccountIds = null,
    Guid? RestrictToUserId = null,
    Guid? ActorUserId = null,
    string CorrelationId = ""
);
