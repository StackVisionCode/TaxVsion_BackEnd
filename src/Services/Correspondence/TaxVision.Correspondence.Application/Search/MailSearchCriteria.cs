namespace TaxVision.Correspondence.Application.Search;

/// <summary>
/// Qué buscar y con qué permisos. <see cref="VisibleAccountIds"/> null = todos los buzones;
/// <see cref="RestrictToUserId"/> null = todos los clientes (<c>customers.view_all</c>);
/// <see cref="DraftOwnerUserId"/> = solo los borradores propios (null = los de todos, quien ve el buzón de oficina).
/// <see cref="Limit"/> se aplica por grupo. Texto: <see cref="Term"/> (en cualquier parte) y/o los campos
/// <see cref="From"/>, <see cref="To"/>, <see cref="Subject"/>; todo lo que venga se combina con Y. Filtros
/// opcionales: <see cref="CustomerId"/> o <see cref="Unlinked"/> (sin cliente), rango de fechas inclusivo y
/// <see cref="Folder"/>; <see cref="HasAttachmentsOnly"/> y <see cref="UnreadOnly"/> no filtran archivos.
/// <see cref="SenderAccountIds"/> = buzones cuya dirección o nombre contiene <see cref="From"/> (así se filtra lo
/// saliente); <see cref="OwnMailboxAddresses"/> = direcciones de los buzones del tenant, en minúsculas, que nunca
/// salen como personas. Los dos los resuelve el handler.
/// </summary>
public sealed record MailSearchCriteria(
    Guid TenantId,
    string? Term,
    int Limit,
    IReadOnlyCollection<Guid>? VisibleAccountIds,
    Guid? RestrictToUserId,
    Guid? DraftOwnerUserId,
    Guid? CustomerId = null,
    bool Unlinked = false,
    bool HasAttachmentsOnly = false,
    bool UnreadOnly = false,
    DateTime? FromUtc = null,
    DateTime? ToUtc = null,
    string? From = null,
    string? To = null,
    string? Subject = null,
    MailSearchFolder? Folder = null,
    MailSearchGroups Groups = MailSearchGroups.All,
    IReadOnlyCollection<Guid>? SenderAccountIds = null,
    IReadOnlyCollection<string>? OwnMailboxAddresses = null
)
{
    public bool InRange(DateTime value) => (FromUtc is null || value >= FromUtc) && (ToUtc is null || value <= ToUtc);

    /// <summary>¿La carpeta pedida abarca <paramref name="folder"/>? Sin carpeta = todas.</summary>
    public bool Includes(MailSearchFolder folder) => Folder is null || Folder == folder;

    public bool Wants(MailSearchGroups group) => (Groups & group) == group;
}

/// <summary>Carpetas de <c>folder=</c> (mismos nombres que las de <see cref="MailSearchValues"/>).</summary>
public enum MailSearchFolder
{
    Inbox,
    Archived,
    Sent,
    Drafts,
    Trash,
}

/// <summary>Grupos de <c>groups=</c>: solo se calcula lo que se pide.</summary>
[Flags]
public enum MailSearchGroups
{
    None = 0,
    Emails = 1,
    Files = 2,
    People = 4,
    All = Emails | Files | People,
}
