using TaxVision.Notification.Application.Directory.Abstractions;
using TaxVision.Notification.Domain.Directory;

namespace TaxVision.Notification.Tests;

/// <summary>
/// Stub de ICustomerContactPolicyRepository que siempre devuelve null — equivale al default
/// "ambos canales ON" (opt-out) para los tests que no quieren ejercitar el gate T7.3.
/// </summary>
internal sealed class AllowAllContactPolicyRepository : ICustomerContactPolicyRepository
{
    public static readonly AllowAllContactPolicyRepository Instance = new();

    public Task<CustomerContactPolicyProjection?> GetByCustomerIdAsync(
        Guid tenantId,
        Guid customerId,
        CancellationToken ct = default
    ) => Task.FromResult<CustomerContactPolicyProjection?>(null);

    public Task AddAsync(CustomerContactPolicyProjection row, CancellationToken ct = default) => Task.CompletedTask;
}
