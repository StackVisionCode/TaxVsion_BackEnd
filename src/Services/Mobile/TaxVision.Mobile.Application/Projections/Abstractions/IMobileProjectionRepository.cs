using TaxVision.Mobile.Domain.Projections;

namespace TaxVision.Mobile.Application.Projections.Abstractions;

public interface IMobileProjectionRepository
{
    Task<UserPermissionsProjection?> FindUserPermissionsAsync(
        Guid tenantId,
        Guid userId,
        CancellationToken ct = default
    );
    void AddUserPermissions(UserPermissionsProjection projection);

    Task<RolePermissionsProjection?> FindRolePermissionsAsync(
        Guid tenantId,
        Guid roleId,
        CancellationToken ct = default
    );
    void AddRolePermissions(RolePermissionsProjection projection);
}
