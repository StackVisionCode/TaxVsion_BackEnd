using BuildingBlocks.Common;
using Microsoft.EntityFrameworkCore;
using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Domain.Compose;

namespace TaxVision.Correspondence.Infrastructure.Persistence.Repositories;

public sealed class DraftRepository(CorrespondenceDbContext db) : IDraftRepository
{
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 100;

    public Task<Draft?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default) =>
        db
            .Drafts.IgnoreQueryFilters()
            .Include(d => d.Recipients)
            .FirstOrDefaultAsync(d => d.TenantId == tenantId && d.Id == id, ct);

    /// <summary>
    /// <see cref="Draft.ReplyContext"/> vive en una columna JSON (plan §23) — no hay forma de
    /// filtrar por <c>ReplyContext.IncomingEmailId</c> en SQL sin recurrir a JSON_VALUE/OPENJSON.
    /// En vez de eso, se aprovecha el índice <c>(TenantId, CustomerId, Status, UpdatedAtUtc)</c>
    /// para acotar a los drafts abiertos del customer (en la práctica, ninguno o muy pocos por
    /// customer a la vez) y se filtra en memoria — el mismo trade-off que Postmaster ya acepta
    /// para sus propias columnas JSON (nunca las consulta por contenido, solo las lee tras cargar
    /// la fila por Id).
    /// </summary>
    public async Task<Draft?> FindOpenReplyDraftAsync(
        Guid tenantId,
        Guid customerId,
        Guid incomingEmailId,
        CancellationToken ct = default
    )
    {
        var openDrafts = await db
            .Drafts.IgnoreQueryFilters()
            .Where(d => d.TenantId == tenantId && d.CustomerId == customerId && d.Status == DraftStatus.Draft)
            .ToListAsync(ct);

        return openDrafts.Find(d => d.ReplyContext?.IncomingEmailId == incomingEmailId);
    }

    public async Task AddAsync(Draft entity, CancellationToken ct = default)
    {
        await db.Drafts.AddAsync(entity, ct);
    }

    // AsNoTracking: listado de solo lectura ("retomar autoguardado"), mismo criterio que
    // EmailThreadRepository.ListByCustomerAsync/IncomingEmailRepository.ListByThreadAsync. Usa
    // IX_Drafts_TenantId_CustomerId_Status_UpdatedAtUtc.
    public async Task<PagedResult<Draft>> ListOpenByCustomerAsync(
        Guid tenantId,
        Guid customerId,
        int page,
        int size,
        CancellationToken ct = default
    )
    {
        var normalizedPage = page < 1 ? 1 : page;
        var normalizedSize = ClampPageSize(size);

        var query = db
            .Drafts.AsNoTracking()
            .IgnoreQueryFilters()
            .Where(d => d.TenantId == tenantId && d.CustomerId == customerId && d.Status == DraftStatus.Draft);

        var totalCount = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(d => d.UpdatedAtUtc)
            .Skip((normalizedPage - 1) * normalizedSize)
            .Take(normalizedSize)
            .ToListAsync(ct);

        return new PagedResult<Draft>(items, normalizedPage, normalizedSize, totalCount);
    }

    // Include(Recipients): ListThreadMessagesHandler necesita ToAddresses para el DTO de thread
    // unificado. Sin paginar — ver el comentario de la interfaz. Usa
    // IX_Drafts_TenantId_EmailThreadId_Status.
    public async Task<IReadOnlyList<Draft>> ListSentByThreadAsync(
        Guid tenantId,
        Guid emailThreadId,
        CancellationToken ct = default
    ) =>
        await db
            .Drafts.AsNoTracking()
            .IgnoreQueryFilters()
            .Include(d => d.Recipients)
            .Where(d => d.TenantId == tenantId && d.EmailThreadId == emailThreadId && d.Status == DraftStatus.Sent)
            .ToListAsync(ct);

    // Tracked (sin AsNoTracking): DraftCleanupJob muta cada fila (Draft.Discard()) y guarda por el
    // mismo UnitOfWork/DbContext del scope — a diferencia de los listados de solo lectura de arriba.
    // Usa IX_Drafts_Status_UpdatedAtUtc (Fase 16, ver el WHY-comment del índice en DraftConfiguration).
    // IgnoreQueryFilters: job cross-tenant (RBAC Fase 5) — recorre drafts abandonados de todos los
    // tenants, nunca sirve una request autenticada.
    public async Task<IReadOnlyList<Draft>> ListAbandonedAsync(
        DateTime updatedBeforeUtc,
        int limit,
        CancellationToken ct = default
    ) =>
        await db
            .Drafts.IgnoreQueryFilters()
            .Where(d => d.Status == DraftStatus.Draft && d.UpdatedAtUtc < updatedBeforeUtc)
            .OrderBy(d => d.UpdatedAtUtc)
            .Take(limit)
            .ToListAsync(ct);

    private static int ClampPageSize(int requested) =>
        requested switch
        {
            < 1 => DefaultPageSize,
            > MaxPageSize => MaxPageSize,
            _ => requested,
        };
}
