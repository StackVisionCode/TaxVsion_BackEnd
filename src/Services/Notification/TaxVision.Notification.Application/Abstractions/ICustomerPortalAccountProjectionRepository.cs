using TaxVision.Notification.Domain.Recipients;

namespace TaxVision.Notification.Application.Abstractions;

public interface ICustomerPortalAccountProjectionRepository
{
    /// <summary>El usuario de la cuenta de portal activa del cliente, si está proyectada. <c>null</c> = no existe.</summary>
    Task<Guid?> GetActivePortalUserIdAsync(Guid tenantId, Guid customerId, CancellationToken ct = default);

    Task<CustomerPortalAccountProjection?> GetByCustomerAsync(
        Guid tenantId,
        Guid customerId,
        CancellationToken ct = default
    );

    Task<CustomerPortalAccountProjection?> GetByUserAsync(Guid tenantId, Guid userId, CancellationToken ct = default);

    Task AddAsync(CustomerPortalAccountProjection projection, CancellationToken ct = default);
}
