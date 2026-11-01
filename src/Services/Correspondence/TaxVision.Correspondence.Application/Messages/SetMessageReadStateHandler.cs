using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Application.Threads;
using Wolverine;

namespace TaxVision.Correspondence.Application.Messages;

/// <summary>
/// Marca un correo inbound como leído/no-leído (estado compartido por el tenant) — HTTP-triggered,
/// no un consumer Wolverine, mismo criterio que el resto de handlers del inbox (no empuja
/// correlación). Idempotente: <see cref="Domain.Inbox.IncomingEmail.MarkRead"/>/<c>MarkUnread</c>
/// son no-op si ya está en ese estado, así que repetir la acción no reescribe ni falla.
/// </summary>
public static class SetMessageReadStateHandler
{
    public static async Task<Result> Handle(
        SetMessageReadStateCommand command,
        IIncomingEmailRepository incomingEmails,
        IEmailThreadRepository emailThreads,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var email = await incomingEmails.GetByIdAsync(command.TenantId, command.IncomingEmailId, ct);
        var thread = email is null ? null : await emailThreads.GetByIdAsync(command.TenantId, email.EmailThreadId, ct);
        if (
            email is null
            || thread is null
            || !await ThreadAccess.CanSeeAsync(
                emailThreads,
                thread,
                command.VisibleAccountIds,
                command.RestrictToUserId,
                ct
            )
        )
            return Result.Failure(MessageNotFound);

        var changed = command.IsRead ? email.MarkRead(DateTime.UtcNow) : email.MarkUnread();
        if (changed)
        {
            await unitOfWork.SaveChangesAsync(ct);
            await ThreadStateNotifier.PublishAsync(
                bus,
                incomingEmails,
                thread,
                command.ActorUserId,
                command.CorrelationId,
                ct
            );
        }

        return Result.Success();
    }

    private static readonly Error MessageNotFound = new(
        "IncomingEmail.NotFound",
        "The message was not found for this tenant."
    );
}
