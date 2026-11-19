using BuildingBlocks.CustomerVisibility;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Application.Requests.Queries.Work;
using TaxVision.Signature.Domain.Requests;

namespace TaxVision.Signature.Infrastructure.Persistence.Queries;

/// <summary>
/// Filas del trabajo de firmas: enviadas o expiradas hace poco donde la persona es autora o preparadora sin firmar.
/// Tenant explícito (IgnoreQueryFilters: el scope de Wolverine no trae tenant ambiente) y el mismo filtro de
/// asignación que <see cref="SignatureRequestReadService"/> cuando quien mira no ve todo.
/// </summary>
internal sealed class SignatureWorkReadService(SignatureDbContext db, IOptions<SignatureVisibilityOptions> visibility)
    : ISignatureWorkReadService
{
    public async Task<IReadOnlyList<SignatureWorkRow>> ListAsync(
        SignatureWorkFilter filter,
        CancellationToken ct = default
    )
    {
        var userIds = filter.UserIds.Distinct().ToList();
        if (userIds.Count == 0)
            return [];

        var preparerIds = userIds.Select(id => (Guid?)id).ToList();
        var tenantId = filter.TenantId;
        var expiredSince = filter.ExpiredSinceUtc;

        var query = db
            .SignatureRequests.AsNoTracking()
            .IgnoreQueryFilters()
            .Where(r => r.TenantId == tenantId)
            .Where(r =>
                r.Status == SignatureRequestStatus.InProgress
                || (r.Status == SignatureRequestStatus.Expired && r.ExpiredAtUtc >= expiredSince)
            )
            .Where(r =>
                userIds.Contains(r.CreatedByUserId)
                || (r.Preparer != null && preparerIds.Contains(r.Preparer.UserId) && r.PreparerSignedByUserId == null)
            );

        if (visibility.Value.Enabled && !filter.CanViewAll)
        {
            var actor = filter.ActorUserId;
            query = query.Where(r =>
                db.Set<CustomerAssignmentProjection>()
                    .IgnoreQueryFilters()
                    .Any(a =>
                        a.TenantId == tenantId
                        && a.UserId == actor
                        && r.Signers.Any(s => s.MappedCustomerId == a.CustomerId)
                    )
            );
        }

        return await query
            .Select(r => new SignatureWorkRow(
                r.Id,
                r.Title,
                r.Status,
                r.CreatedByUserId,
                r.Preparer != null ? r.Preparer.UserId : null,
                r.PreparerSignedByUserId != null,
                r.ExpiresAtUtc,
                r.Signers.Where(s => s.MappedCustomerId != null)
                    .OrderBy(s => s.Order)
                    .Select(s => s.MappedCustomerId)
                    .FirstOrDefault()
            ))
            .ToListAsync(ct);
    }

    // Mismo criterio que el listado: con el filtro encendido y sin view_all, cliente asignado o borrador propio.
    public async Task<IReadOnlyList<SignatureTitleRow>> ListTitlesAsync(
        SignatureTitleFilter filter,
        CancellationToken ct = default
    )
    {
        var ids = filter.Ids.Distinct().ToList();
        if (ids.Count == 0)
            return [];

        var tenantId = filter.TenantId;
        var query = db
            .SignatureRequests.AsNoTracking()
            .IgnoreQueryFilters()
            .Where(r => r.TenantId == tenantId && ids.Contains(r.Id));

        if (visibility.Value.Enabled && !filter.CanViewAll)
        {
            var actor = filter.ActorUserId;
            query = query.Where(r =>
                (r.Status == SignatureRequestStatus.Draft && r.CreatedByUserId == actor)
                || db.Set<CustomerAssignmentProjection>()
                    .IgnoreQueryFilters()
                    .Any(a =>
                        a.TenantId == tenantId
                        && a.UserId == actor
                        && r.Signers.Any(s => s.MappedCustomerId == a.CustomerId)
                    )
            );
        }

        return await query.Select(r => new SignatureTitleRow(r.Id, r.Title)).ToListAsync(ct);
    }
}
