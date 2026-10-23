using Microsoft.EntityFrameworkCore;
using TaxVision.Tasks.ClientForms.Application.Participants.Abstractions;
using TaxVision.Tasks.Infrastructure.Persistence;

namespace TaxVision.Tasks.Infrastructure.ClientForms.Persistence.Repositories;

internal sealed class FormResponsibleUserValidator(TasksDbContext context) : IFormResponsibleUserValidator
{
    public Task<bool> IsActiveAsync(Guid tenantId, Guid userId, CancellationToken ct = default) =>
        context.UserPermissionsProjections.IgnoreQueryFilters().AnyAsync(
            item => item.TenantId == tenantId && item.UserId == userId && item.IsActive,
            ct
        );
}
