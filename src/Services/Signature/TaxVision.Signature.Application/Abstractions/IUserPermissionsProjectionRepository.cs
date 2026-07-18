using TaxVision.Signature.Domain.Projections;

namespace TaxVision.Signature.Application.Abstractions;

public interface IUserPermissionsProjectionRepository
{
    Task<UserPermissionsProjection?> GetAsync(Guid tenantId, Guid userId, CancellationToken ct = default);

    Task AddAsync(UserPermissionsProjection projection, CancellationToken ct = default);
}
