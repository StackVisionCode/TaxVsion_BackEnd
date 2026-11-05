using BuildingBlocks.Common;
using Microsoft.EntityFrameworkCore;
using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Domain.Inbox;

namespace TaxVision.Correspondence.Infrastructure.Persistence.Repositories;

public sealed class IncomingEmailRepository(CorrespondenceDbContext db) : IIncomingEmailRepository
{
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 100;

    public Task<IncomingEmail?> FindByInternetMessageIdAsync(
        Guid tenantId,
        string internetMessageId,
        CancellationToken ct = default
    ) =>
        db
            .IncomingEmails.IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.InternetMessageId == internetMessageId, ct);

    public Task<IncomingEmail?> FindByProviderMessageIdAsync(
        Guid tenantId,
        Guid accountId,
        string providerMessageId,
        CancellationToken ct = default
    ) =>
        db
            .IncomingEmails.IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                x =>
                    x.TenantId == tenantId
                    && x.AccountId == accountId
                    && x.ProviderMessageId == providerMessageId
                    && x.InternetMessageId == null,
                ct
            );

    // Include(Attachments) desde Fase 7 (ListMessageAttachmentsHandler necesita la colección
    // hidratada); Fase 5 (GetMessageBodyHandler) no la usa pero tampoco le molesta cargarla, son
    // pocas filas de metadata, nunca binarios.
    public Task<IncomingEmail?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default) =>
        db
            .IncomingEmails.IgnoreQueryFilters()
            .Include(x => x.Attachments)
            .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == id, ct);

    public async Task<IReadOnlyList<IncomingEmail>> ListInForeignThreadsAsync(
        Guid tenantId,
        CancellationToken ct = default
    ) =>
        await db
            .IncomingEmails.IgnoreQueryFilters()
            .Where(ie =>
                ie.TenantId == tenantId
                && db.EmailThreads.IgnoreQueryFilters()
                    .Any(t => t.Id == ie.EmailThreadId && t.CustomerId != null && t.CustomerId != ie.CustomerId)
            )
            .ToListAsync(ct);

    public async Task AddAsync(IncomingEmail entity, CancellationToken ct = default)
    {
        await db.IncomingEmails.AddAsync(entity, ct);
    }

    public async Task<PagedResult<IncomingEmail>> ListByThreadAsync(
        Guid tenantId,
        Guid emailThreadId,
        int page,
        int size,
        CancellationToken ct = default
    )
    {
        var normalizedPage = page < 1 ? 1 : page;
        var normalizedSize = ClampPageSize(size);

        // AsNoTracking + sin Include(Attachments): esto alimenta un listado de metadata, no
        // necesita la colección hidratada (a diferencia de GetByIdAsync, que sí la usa para
        // GetMessageBodyHandler/ListMessageAttachmentsHandler). Usa
        // IX_IncomingEmails_TenantId_EmailThreadId_ReceivedAtUtc.
        var query = db
            .IncomingEmails.AsNoTracking()
            .IgnoreQueryFilters()
            .Where(x => x.TenantId == tenantId && x.EmailThreadId == emailThreadId && x.DeletedAtUtc == null);

        var totalCount = await query.CountAsync(ct);

        var items = await query
            .OrderBy(x => x.ReceivedAtUtc)
            .Skip((normalizedPage - 1) * normalizedSize)
            .Take(normalizedSize)
            .ToListAsync(ct);

        return new PagedResult<IncomingEmail>(items, normalizedPage, normalizedSize, totalCount);
    }

    // AsNoTracking, sin Skip/Take — ver el comentario de la interfaz sobre por qué esto es seguro
    // para un hilo puntual (a diferencia de un listado por customer). Mismo índice que
    // ListByThreadAsync (IX_IncomingEmails_TenantId_EmailThreadId_ReceivedAtUtc).
    public async Task<IReadOnlyList<IncomingEmail>> ListAllByThreadAsync(
        Guid tenantId,
        Guid emailThreadId,
        CancellationToken ct = default
    ) =>
        await db
            .IncomingEmails.AsNoTracking()
            .IgnoreQueryFilters()
            .Where(x => x.TenantId == tenantId && x.EmailThreadId == emailThreadId)
            .OrderBy(x => x.ReceivedAtUtc)
            .ToListAsync(ct);

    public async Task<IReadOnlyDictionary<Guid, IncomingEmail>> GetLatestByThreadAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> emailThreadIds,
        CancellationToken ct = default
    )
    {
        if (emailThreadIds.Count == 0)
            return new Dictionary<Guid, IncomingEmail>();

        // Una fila por hilo (ROW_NUMBER en SQL Server): el entrante vivo más reciente.
        var latest = await db
            .IncomingEmails.AsNoTracking()
            .IgnoreQueryFilters()
            .Where(x => x.TenantId == tenantId && x.DeletedAtUtc == null && emailThreadIds.Contains(x.EmailThreadId))
            .GroupBy(x => x.EmailThreadId)
            .Select(g => g.OrderByDescending(x => x.ReceivedAtUtc).First())
            .ToListAsync(ct);
        return latest.ToDictionary(x => x.EmailThreadId);
    }

    public async Task<IReadOnlySet<Guid>> GetThreadIdsWithAttachmentsAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> emailThreadIds,
        CancellationToken ct = default
    )
    {
        if (emailThreadIds.Count == 0)
            return new HashSet<Guid>();

        var ids = await db
            .IncomingEmails.AsNoTracking()
            .IgnoreQueryFilters()
            .Where(x =>
                x.TenantId == tenantId
                && x.DeletedAtUtc == null
                && x.HasAttachments
                && emailThreadIds.Contains(x.EmailThreadId)
            )
            .Select(x => x.EmailThreadId)
            .Distinct()
            .ToListAsync(ct);

        // También cuentan los adjuntos de lo enviado en el hilo (la fila y el filtro "Has attachments" coinciden).
        var sent = await db
            .Drafts.AsNoTracking()
            .IgnoreQueryFilters()
            .Where(d =>
                d.TenantId == tenantId
                && d.Status == Domain.Compose.DraftStatus.Sent
                && d.DeletedAtUtc == null
                && d.EmailThreadId != null
                && emailThreadIds.Contains(d.EmailThreadId.Value)
                && d.Attachments.Any()
            )
            .Select(d => d.EmailThreadId!.Value)
            .Distinct()
            .ToListAsync(ct);
        return ids.Concat(sent).ToHashSet();
    }

    public async Task<IReadOnlyDictionary<Guid, int>> GetUnreadCountsByThreadAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> emailThreadIds,
        CancellationToken ct = default
    )
    {
        if (emailThreadIds.Count == 0)
            return new Dictionary<Guid, int>();

        // GROUP BY sobre el índice filtrado IX_..._Unread: solo cuenta las filas no leídas.
        var counts = await db
            .IncomingEmails.AsNoTracking()
            .IgnoreQueryFilters()
            .Where(x =>
                x.TenantId == tenantId
                && !x.IsRead
                && x.DeletedAtUtc == null
                && emailThreadIds.Contains(x.EmailThreadId)
            )
            .GroupBy(x => x.EmailThreadId)
            .Select(g => new { EmailThreadId = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        // No leídos del HILO: también cuentan los avisos de no entrega sin leer (D-4: los Delayed nacen leídos).
        var notices = await MailVisibilityQueries
            .UnreadNotices(db, tenantId)
            .Where(n => emailThreadIds.Contains(n.EmailThreadId))
            .GroupBy(n => n.EmailThreadId)
            .Select(g => new { EmailThreadId = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var result = counts.ToDictionary(x => x.EmailThreadId, x => x.Count);
        foreach (var notice in notices)
            result[notice.EmailThreadId] = result.GetValueOrDefault(notice.EmailThreadId) + notice.Count;
        return result;
    }

    // TRACKED a propósito (sin AsNoTracking): el caller muta IsRead y persiste. Sin Include de
    // adjuntos — marcar leído no los toca. Acotado por hilo, mismo criterio que ListAllByThreadAsync.
    public async Task<IReadOnlyList<IncomingEmail>> ListByThreadForUpdateAsync(
        Guid tenantId,
        Guid emailThreadId,
        CancellationToken ct = default
    ) =>
        await db
            .IncomingEmails.IgnoreQueryFilters()
            .Where(x => x.TenantId == tenantId && x.EmailThreadId == emailThreadId)
            .ToListAsync(ct);

    // Papelera del customer (solo entrantes borrados), más reciente primero.
    public async Task<PagedResult<IncomingEmail>> ListTrashedByCustomerAsync(
        Guid tenantId,
        Guid? customerId,
        Guid? restrictToUserId,
        int page,
        int size,
        IReadOnlyCollection<Guid>? visibleAccountIds = null,
        MailListFilter? filter = null,
        CancellationToken ct = default
    )
    {
        var normalizedPage = page < 1 ? 1 : page;
        var normalizedSize = ClampPageSize(size);

        var query = db
            .IncomingEmails.AsNoTracking()
            .IgnoreQueryFilters()
            .Where(x => x.TenantId == tenantId && x.DeletedAtUtc != null);
        if (customerId is { } id)
            query = query.Where(x => x.CustomerId == id);

        // Cliente asignado + gate de buzón de oficina: oculta entrantes borrados de buzones no visibles.
        query = MailVisibilityQueries.IncomingOfVisibleCustomers(
            db,
            query,
            tenantId,
            restrictToUserId,
            visibleAccountIds
        );
        query = MailVisibilityQueries.FilterIncoming(query, filter);

        var totalCount = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(x => x.DeletedAtUtc)
            .Skip((normalizedPage - 1) * normalizedSize)
            .Take(normalizedSize)
            .ToListAsync(ct);

        return new PagedResult<IncomingEmail>(items, normalizedPage, normalizedSize, totalCount);
    }

    public void Remove(IncomingEmail entity) => db.IncomingEmails.Remove(entity);

    // Tracked (sin AsNoTracking): el caller marca el attachment Blocked y persiste.
    public Task<IncomingEmail?> FindByAttachmentCloudStorageFileIdAsync(
        Guid tenantId,
        Guid cloudStorageFileId,
        CancellationToken ct = default
    ) =>
        db
            .IncomingEmails.IgnoreQueryFilters()
            .Include(x => x.Attachments)
            .FirstOrDefaultAsync(
                x => x.TenantId == tenantId && x.Attachments.Any(a => a.CloudStorageFileId == cloudStorageFileId),
                ct
            );

    private static int ClampPageSize(int requested) =>
        requested switch
        {
            < 1 => DefaultPageSize,
            > MaxPageSize => MaxPageSize,
            _ => requested,
        };
}
