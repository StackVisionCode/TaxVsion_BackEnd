using BuildingBlocks.CustomerVisibility;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Application.Requests.Queries.List;
using TaxVision.Signature.Domain.Requests;

namespace TaxVision.Signature.Infrastructure.Persistence.Queries;

internal sealed class SignatureRequestReadService(
    SignatureDbContext db,
    IOptions<SignatureVisibilityOptions> visibility
) : ISignatureRequestReadService
{
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 100;

    public async Task<ListSignatureRequestsResult> ListAsync(
        ListSignatureRequestsQuery query,
        CancellationToken ct = default
    )
    {
        var page = query.Page < 1 ? 1 : query.Page;
        var pageSize = ClampPageSize(query.PageSize);

        // Este query se despacha vía bus.InvokeAsync de Wolverine, que corre en un scope de DI
        // distinto al del request HTTP — el TenantContext ambiente que puebla el filtro global de
        // SignatureDbContext no está seteado ahí. query.TenantId ya viene explícito y confiable
        // desde el controller — IgnoreQueryFilters() explícito.
        var baseQuery = db
            .SignatureRequests.AsNoTracking()
            .IgnoreQueryFilters()
            .Where(r => r.TenantId == query.TenantId);

        if (query.Status.HasValue)
            baseQuery = baseQuery.Where(r => r.Status == query.Status.Value);
        if (query.Category is not null)
            baseQuery = baseQuery.Where(r => r.Category == query.Category);
        if (query.EditableOnly)
            baseQuery = baseQuery.Where(r =>
                r.Status == SignatureRequestStatus.Draft || r.Status == SignatureRequestStatus.Ready
            );

        // Visibilidad por asignación (P2): solo solicitudes cuyo cliente está asignado al actor. La request
        // no tiene CustomerId → se atraviesa Signers.MappedCustomerId. IgnoreQueryFilters + tenant explícito
        // en el subquery (scope de Wolverine sin tenant ambiental). null = sin restricción (view_all/flag off).
        var assignedTo = visibility.Value.Enabled && !query.CanViewAll ? query.ActorUserId : (Guid?)null;
        if (assignedTo is { } assignee)
            baseQuery = baseQuery.Where(r =>
                db.Set<CustomerAssignmentProjection>()
                    .IgnoreQueryFilters()
                    .Any(a =>
                        a.TenantId == query.TenantId
                        && a.UserId == assignee
                        && r.Signers.Any(s => s.MappedCustomerId == a.CustomerId)
                    )
            );

        var total = await baseQuery.CountAsync(ct);

        var items = await baseQuery
            .OrderByDescending(r => r.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(r => new SignatureRequestSummary(
                r.Id,
                r.Title,
                r.Category,
                r.Status,
                r.OriginalFileId,
                r.Signers.Count,
                r.ExpiresAtUtc,
                r.CreatedAtUtc,
                r.SentAtUtc,
                r.CompletedAtUtc
            ))
            .ToListAsync(ct);

        return new ListSignatureRequestsResult(items, total, page, pageSize);
    }

    private static int ClampPageSize(int requested) =>
        requested switch
        {
            < 1 => DefaultPageSize,
            > MaxPageSize => MaxPageSize,
            _ => requested,
        };
}
