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

    // Include(Attachments) desde Fase 7 (ListMessageAttachmentsHandler necesita la colección
    // hidratada); Fase 5 (GetMessageBodyHandler) no la usa pero tampoco le molesta cargarla, son
    // pocas filas de metadata, nunca binarios.
    public Task<IncomingEmail?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default) =>
        db
            .IncomingEmails.IgnoreQueryFilters()
            .Include(x => x.Attachments)
            .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == id, ct);

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
            .Where(x => x.TenantId == tenantId && x.EmailThreadId == emailThreadId);

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

    private static int ClampPageSize(int requested) =>
        requested switch
        {
            < 1 => DefaultPageSize,
            > MaxPageSize => MaxPageSize,
            _ => requested,
        };
}
