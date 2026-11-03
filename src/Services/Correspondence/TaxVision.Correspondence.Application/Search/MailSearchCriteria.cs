namespace TaxVision.Correspondence.Application.Search;

/// <summary>
/// Qué buscar y con qué permisos. <see cref="VisibleAccountIds"/> null = todos los buzones;
/// <see cref="RestrictToUserId"/> null = todos los clientes (<c>customers.view_all</c>);
/// <see cref="DraftOwnerUserId"/> = solo los borradores propios (null = los de todos, quien ve el buzón de oficina).
/// <see cref="Limit"/> se aplica por grupo (correos y archivos). Filtros opcionales: <see cref="CustomerId"/> o
/// <see cref="Unlinked"/> (sin cliente), rango de fechas inclusivo; <see cref="HasAttachmentsOnly"/> y
/// <see cref="UnreadOnly"/> solo afectan a los correos (un archivo siempre es un adjunto y no tiene leído).
/// </summary>
public sealed record MailSearchCriteria(
    Guid TenantId,
    string Term,
    int Limit,
    IReadOnlyCollection<Guid>? VisibleAccountIds,
    Guid? RestrictToUserId,
    Guid? DraftOwnerUserId,
    Guid? CustomerId = null,
    bool Unlinked = false,
    bool HasAttachmentsOnly = false,
    bool UnreadOnly = false,
    DateTime? FromUtc = null,
    DateTime? ToUtc = null
)
{
    public bool InRange(DateTime value) => (FromUtc is null || value >= FromUtc) && (ToUtc is null || value <= ToUtc);
}
