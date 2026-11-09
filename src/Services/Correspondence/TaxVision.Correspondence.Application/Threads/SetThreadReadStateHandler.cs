using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Correspondence.Application.Abstractions;

namespace TaxVision.Correspondence.Application.Threads;

/// <summary>
/// Marca todos los correos inbound (y los avisos de no entrega) de un hilo como leídos/no-leídos (estado compartido por el
/// tenant) — HTTP-triggered. Confirma primero que el hilo existe y es del tenant (404 si no), mismo
/// criterio de "confirmar tenencia antes de mutar hijos" que <see cref="ListThreadMessagesHandler"/>.
/// Solo persiste si algún correo cambió de estado (los <c>MarkRead/Unread</c> son idempotentes).
/// </summary>
public static class SetThreadReadStateHandler
{
    public static async Task<Result> Handle(
        SetThreadReadStateCommand command,
        IEmailThreadRepository emailThreads,
        IIncomingEmailRepository incomingEmails,
        IDeliveryNoticeRepository notices,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var thread = await emailThreads.GetByIdAsync(command.TenantId, command.ThreadId, ct);
        if (
            thread is null
            || !await ThreadAccess.CanSeeAsync(
                emailThreads,
                thread,
                command.VisibleAccountIds,
                command.RestrictToUserId,
                ct
            )
        )
            return Result.Failure(ThreadAccess.NotFound);

        var emails = await incomingEmails.ListByThreadForUpdateAsync(command.TenantId, command.ThreadId, ct);
        var now = DateTime.UtcNow;

        var anyChanged = false;
        foreach (var email in emails)
        {
            var changed = command.IsRead ? email.MarkRead(now) : email.MarkUnread();
            anyChanged |= changed;
        }

        // Los avisos de no entrega cuentan como no leídos del hilo: el mismo "marcar todo" los cubre.
        foreach (var notice in await notices.ListByThreadForUpdateAsync(command.TenantId, command.ThreadId, ct))
            anyChanged |= command.IsRead ? notice.MarkRead(now) : notice.MarkUnread();

        if (anyChanged)
        {
            thread.NotifyStateChanged(command.ActorUserId, command.CorrelationId);
            await unitOfWork.SaveChangesAsync(ct);
        }

        return Result.Success();
    }
}
