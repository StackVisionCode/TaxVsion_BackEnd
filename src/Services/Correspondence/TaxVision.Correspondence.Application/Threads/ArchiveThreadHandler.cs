using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Correspondence.Application.Abstractions;
using Wolverine;

namespace TaxVision.Correspondence.Application.Threads;

/// <summary>
/// Archiva un hilo (Fase 9) — HTTP-triggered, no un consumer Wolverine, mismo criterio que el
/// resto de los handlers de esta fase (no empuja correlación). <see cref="Domain.Inbox.EmailThread.Archive"/>
/// (Fase 3) ya es idempotente por diseño: llamarlo sobre un hilo ya archivado es un no-op que no
/// pisa el <c>ArchivedAtUtc</c> original, así que este handler no distingue el caso — siempre
/// llama al aggregate y persiste, sea la primera vez o la enésima.
/// </summary>
public static class ArchiveThreadHandler
{
    public static async Task<Result> Handle(
        ArchiveThreadCommand command,
        IEmailThreadRepository emailThreads,
        IIncomingEmailRepository incomingEmails,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
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

        thread.Archive();

        // Archivar marca el hilo leído (quita la burbuja de no-leído).
        var now = DateTime.UtcNow;
        var emails = await incomingEmails.ListByThreadForUpdateAsync(command.TenantId, command.ThreadId, ct);
        foreach (var email in emails)
            email.MarkRead(now);

        await unitOfWork.SaveChangesAsync(ct);
        await ThreadStateNotifier.PublishAsync(
            bus,
            incomingEmails,
            thread,
            command.ActorUserId,
            command.CorrelationId,
            ct
        );
        return Result.Success();
    }
}
