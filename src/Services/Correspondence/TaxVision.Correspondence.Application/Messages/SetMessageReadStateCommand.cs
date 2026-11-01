namespace TaxVision.Correspondence.Application.Messages;

/// <summary>
/// Fija el estado leído/no-leído de UN correo inbound (compartido por el tenant). Un solo comando
/// con <see cref="IsRead"/> en vez de dos (read/unread): la responsabilidad es la misma, los dos
/// endpoints HTTP solo cambian el booleano.
/// <para>
/// Gate: <paramref name="VisibleAccountIds"/> (buzón visible, <c>null</c> = todos) y <paramref name="RestrictToUserId"/>
/// (cliente asignado, <c>null</c> = todos). <paramref name="ActorUserId"/> y <paramref name="CorrelationId"/>
/// (del scope HTTP: el de Wolverine llega vacío) viajan en el aviso en vivo del cambio.
/// </para>
/// </summary>
public sealed record SetMessageReadStateCommand(
    Guid TenantId,
    Guid IncomingEmailId,
    bool IsRead,
    IReadOnlyCollection<Guid>? VisibleAccountIds = null,
    Guid? RestrictToUserId = null,
    Guid? ActorUserId = null,
    string CorrelationId = ""
);
