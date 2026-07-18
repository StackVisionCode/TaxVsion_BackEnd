using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Correspondence.Application.Abstractions;

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
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var thread = await emailThreads.GetByIdAsync(command.TenantId, command.ThreadId, ct);
        if (thread is null)
            return Result.Failure(new Error("EmailThread.NotFound", "The thread was not found for this tenant."));

        thread.Archive();
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
