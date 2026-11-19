using TaxVision.Customer.Domain.Customers;

namespace TaxVision.Customer.Application.Abstractions;

/// <summary>
/// Lecturas del Work Profile: clientes activos a cargo de una persona. <c>visibleTo</c> = ABAC del que mira
/// (solo clientes asignados a él); null = sin restricción. Siempre por tenant.
/// </summary>
public interface ICustomerWorkReadService
{
    Task<CustomerWorkCounts> CountAsync(Guid tenantId, Guid userId, Guid? visibleTo, CancellationToken ct);

    /// <summary>Clientes como preparador, los más recientes primero.</summary>
    Task<IReadOnlyList<CustomerWorkRow>> TopAsPreparerAsync(
        Guid tenantId,
        Guid userId,
        Guid? visibleTo,
        int take,
        CancellationToken ct
    );

    /// <summary>Clientes como preparador por persona; las que no tienen ninguno no aparecen.</summary>
    Task<IReadOnlyDictionary<Guid, int>> CountAsPreparerAsync(
        Guid tenantId,
        IReadOnlyList<Guid> userIds,
        Guid? visibleTo,
        CancellationToken ct
    );
}

public sealed record CustomerWorkCounts(int AsPreparer, int Assigned);

public sealed record CustomerWorkRow(Guid Id, string DisplayName, CustomerStatus Status);
