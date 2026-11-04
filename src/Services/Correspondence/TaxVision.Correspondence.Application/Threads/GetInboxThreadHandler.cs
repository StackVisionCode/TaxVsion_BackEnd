using BuildingBlocks.Results;
using TaxVision.Correspondence.Application.Abstractions;

namespace TaxVision.Correspondence.Application.Threads;

/// <summary>
/// <paramref name="VisibleAccountIds"/> null = todos los buzones; <paramref name="RestrictToUserId"/> null = todos los
/// clientes. Quien no ve el hilo recibe NotFound.
/// </summary>
public sealed record GetInboxThreadQuery(
    Guid TenantId,
    Guid ThreadId,
    IReadOnlyCollection<Guid>? VisibleAccountIds = null,
    Guid? RestrictToUserId = null
);

/// <summary>
/// UNA fila del Inbox por id (<c>GET /correspondence/threads/{id}</c>): para abrir un resultado de búsqueda o un
/// aviso que no está en la página cargada. Mismo DTO y mismo gate que el listado.
/// </summary>
public static class GetInboxThreadHandler
{
    public static async Task<Result<InboxThreadSummary>> Handle(
        GetInboxThreadQuery query,
        IEmailThreadRepository emailThreads,
        IIncomingEmailRepository incomingEmails,
        IDeliveryNoticeRepository notices,
        CancellationToken ct
    )
    {
        var thread = await emailThreads.GetByIdAsync(query.TenantId, query.ThreadId, ct);
        if (
            thread is null
            || !await ThreadAccess.CanSeeAsync(
                emailThreads,
                thread,
                query.VisibleAccountIds,
                query.RestrictToUserId,
                ct
            )
        )
            return Result.Failure<InboxThreadSummary>(ThreadAccess.NotFound);

        IReadOnlyCollection<Guid> ids = [thread.Id];
        var unread = await incomingEmails.GetUnreadCountsByThreadAsync(query.TenantId, ids, ct);
        var latest = await incomingEmails.GetLatestByThreadAsync(query.TenantId, ids, ct);
        var withAttachments = await incomingEmails.GetThreadIdsWithAttachmentsAsync(query.TenantId, ids, ct);
        var latestNotice = await notices.GetLatestByThreadAsync(query.TenantId, ids, ct);

        return Result.Success(
            ListInboxThreadsHandler.ToSummary(
                thread,
                unread.GetValueOrDefault(thread.Id, 0),
                latest.GetValueOrDefault(thread.Id),
                withAttachments.Contains(thread.Id),
                latestNotice.GetValueOrDefault(thread.Id)
            )
        );
    }
}
