using Microsoft.EntityFrameworkCore;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Application.Templates.Queries.List;

namespace TaxVision.Signature.Infrastructure.Persistence.Queries;

internal sealed class SignatureTemplateReadService(SignatureDbContext db) : ISignatureTemplateReadService
{
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 100;

    public async Task<ListTemplatesResult> ListAsync(ListTemplatesQuery query, CancellationToken ct = default)
    {
        var page = query.Page < 1 ? 1 : query.Page;
        var pageSize = ClampPageSize(query.PageSize);

        // Mismo bug de scope de Wolverine que SignatureRequestReadService.ListAsync (ver comentario
        // ahí y en LocalCommandTenantMiddleware.cs): query.TenantId ya viene explícito y validado
        // desde el controller — IgnoreQueryFilters() explícito porque el filtro ambiental global
        // puede no estar poblado en este scope de DI.
        var baseQuery = db
            .SignatureTemplates.AsNoTracking()
            .IgnoreQueryFilters()
            .Where(t => t.TenantId == query.TenantId);
        if (query.Status.HasValue)
            baseQuery = baseQuery.Where(t => t.Status == query.Status.Value);
        if (query.Category.HasValue)
            baseQuery = baseQuery.Where(t => t.Category == query.Category.Value);

        var total = await baseQuery.CountAsync(ct);
        var items = await baseQuery
            .OrderByDescending(t => t.UpdatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(t => new TemplateSummary(
                t.Id,
                t.Title,
                t.Category,
                t.Status,
                t.Slots.Count,
                t.Fields.Count,
                t.CreatedAtUtc,
                t.PublishedAtUtc
            ))
            .ToListAsync(ct);

        return new ListTemplatesResult(items, total, page, pageSize);
    }

    private static int ClampPageSize(int requested) =>
        requested switch
        {
            < 1 => DefaultPageSize,
            > MaxPageSize => MaxPageSize,
            _ => requested,
        };
}
