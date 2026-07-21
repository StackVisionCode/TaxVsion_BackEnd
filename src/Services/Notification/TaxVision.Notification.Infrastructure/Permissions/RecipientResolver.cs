using TaxVision.Notification.Application.Abstractions;

namespace TaxVision.Notification.Infrastructure.Permissions;

public sealed class RecipientResolver(IUserPermissionsProjectionRepository userPermissions) : IRecipientResolver
{
    public Task<IReadOnlyList<Guid>> ResolveAsync(ByPermission audience, CancellationToken ct = default) =>
        userPermissions.FindActiveUserIdsByPermissionAsync(audience.TenantId, audience.PermissionCode, ct);
}
