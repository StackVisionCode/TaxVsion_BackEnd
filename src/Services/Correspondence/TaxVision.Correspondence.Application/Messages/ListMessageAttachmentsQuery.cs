namespace TaxVision.Correspondence.Application.Messages;

/// <summary>
/// <paramref name="VisibleAccountIds"/> null = ve todo; si no, el hilo del mensaje debe ser visible o NotFound (gate de
/// buzón de oficina). <paramref name="RestrictToUserId"/> null = todos los clientes; si no, el cliente del mensaje debe
/// estar asignado a ese usuario o NotFound.
/// </summary>
public sealed record ListMessageAttachmentsQuery(
    Guid TenantId,
    Guid IncomingEmailId,
    IReadOnlyCollection<Guid>? VisibleAccountIds = null,
    Guid? RestrictToUserId = null
);
