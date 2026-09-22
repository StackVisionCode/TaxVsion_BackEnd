namespace TaxVision.Correspondence.Application.Messages;

/// <summary><paramref name="VisibleAccountIds"/> null = ve todo; si no, el hilo del mensaje debe ser visible o NotFound (gate de buzón de oficina).</summary>
public sealed record ListMessageAttachmentsQuery(
    Guid TenantId,
    Guid IncomingEmailId,
    IReadOnlyCollection<Guid>? VisibleAccountIds = null
);
