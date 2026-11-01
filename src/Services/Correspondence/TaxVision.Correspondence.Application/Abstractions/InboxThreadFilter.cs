using TaxVision.Correspondence.Domain.Inbox;

namespace TaxVision.Correspondence.Application.Abstractions;

/// <summary>
/// Filtro del inbox de la oficina (hilos de todos los clientes que el usuario puede ver).
/// <see cref="VisibleAccountIds"/> null = todos los buzones; <see cref="RestrictToUserId"/> null = todos los
/// clientes (<c>customers.view_all</c>), si no solo los asignados a ese usuario.
/// </summary>
public sealed record InboxThreadFilter(
    Guid TenantId,
    EmailThreadStatus? Status,
    Guid? CustomerId,
    bool UnreadOnly,
    string? Search,
    int Page,
    int Size,
    IReadOnlyCollection<Guid>? VisibleAccountIds,
    Guid? RestrictToUserId
);
