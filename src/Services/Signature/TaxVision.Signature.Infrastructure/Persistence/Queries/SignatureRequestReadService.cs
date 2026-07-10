using Microsoft.EntityFrameworkCore;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Application.Requests.Queries.List;

namespace TaxVision.Signature.Infrastructure.Persistence.Queries;

internal sealed class SignatureRequestReadService(SignatureDbContext db) : ISignatureRequestReadService
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

        var baseQuery = db.SignatureRequests.AsNoTracking().Where(r => r.TenantId == query.TenantId);

        if (query.Status.HasValue)
            baseQuery = baseQuery.Where(r => r.Status == query.Status.Value);
        if (query.Category.HasValue)
            baseQuery = baseQuery.Where(r => r.Category == query.Category.Value);

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
