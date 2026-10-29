using Microsoft.EntityFrameworkCore;
using TaxVision.Tasks.ClientForms.Application.Participants.Abstractions;
using TaxVision.Tasks.Infrastructure.Persistence;

namespace TaxVision.Tasks.Infrastructure.ClientForms.Persistence.Repositories;

internal sealed class FormResponsibleUserValidator(TasksDbContext context) : IFormResponsibleUserValidator
{
    public Task<bool> IsActiveAsync(Guid tenantId, Guid userId, CancellationToken ct = default) =>
        context
            .UserPermissionsProjections.IgnoreQueryFilters()
            .AnyAsync(item => item.TenantId == tenantId && item.UserId == userId && item.IsActive, ct);

    public async Task<bool> HasPermissionsAsync(
        Guid tenantId,
        Guid userId,
        IReadOnlyCollection<string> permissions,
        CancellationToken ct = default
    )
    {
        var projection = await context
            .UserPermissionsProjections.IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.TenantId == tenantId && item.UserId == userId && item.IsActive, ct);
        if (projection is null)
            return false;
        var granted = projection.PermissionCodes();
        return permissions.All(permission => granted.Contains(permission, StringComparer.Ordinal));
    }
}
