namespace TaxVision.Tenant.Application.Team.Abstractions;

/// <summary>
/// Qué entradas de un miembro se leen. <see cref="AssignedCustomersOf"/> != null limita a las entradas sin
/// cliente o de clientes asignados a ese usuario (ABAC). El rango es semiabierto: [From, To).
/// </summary>
public sealed record ActivityFeedFilter(
    Guid TenantId,
    Guid MemberUserId,
    DateTime? FromUtc,
    DateTime? ToUtc,
    IReadOnlyCollection<string> Verbs,
    Guid? AssignedCustomersOf,
    bool IncludePrivate,
    bool IncludeRestricted,
    int Page,
    int Size
);
