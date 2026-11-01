using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Application.Threads;
using TaxVision.Correspondence.Domain.Inbox;
using Wolverine;

namespace TaxVision.Correspondence.Application.Messages;

/// <summary>Gate: buzón visible y cliente asignado (<c>null</c> = todos), igual que leer el hilo.</summary>
public sealed record TrashIncomingMessageCommand(
    Guid TenantId,
    Guid MessageId,
    IReadOnlyCollection<Guid>? VisibleAccountIds = null,
    Guid? RestrictToUserId = null,
    Guid? ActorUserId = null,
    string CorrelationId = ""
);

/// <summary>Gate: buzón visible y cliente asignado (<c>null</c> = todos), igual que leer el hilo.</summary>
public sealed record RestoreIncomingMessageCommand(
    Guid TenantId,
    Guid MessageId,
    IReadOnlyCollection<Guid>? VisibleAccountIds = null,
    Guid? RestrictToUserId = null,
    Guid? ActorUserId = null,
    string CorrelationId = ""
);

/// <summary>Gate: buzón visible y cliente asignado (<c>null</c> = todos), igual que leer el hilo.</summary>
public sealed record PurgeIncomingMessageCommand(
    Guid TenantId,
    Guid MessageId,
    IReadOnlyCollection<Guid>? VisibleAccountIds = null,
    Guid? RestrictToUserId = null
);

/// <summary>Carga el correo y su hilo, y aplica el gate del hilo (mismo NotFound si no se ve).</summary>
internal static class IncomingMessageAccess
{
    public static readonly Error NotFound = new("IncomingEmail.NotFound", "The message was not found for this tenant.");

    public static async Task<(IncomingEmail Email, EmailThread Thread)?> LoadAsync(
        Guid tenantId,
        Guid messageId,
        IReadOnlyCollection<Guid>? visibleAccountIds,
        Guid? restrictToUserId,
        IIncomingEmailRepository incomingEmails,
        IEmailThreadRepository emailThreads,
        CancellationToken ct
    )
    {
        var email = await incomingEmails.GetByIdAsync(tenantId, messageId, ct);
        if (email is null)
            return null;

        var thread = await emailThreads.GetByIdAsync(tenantId, email.EmailThreadId, ct);
        if (
            thread is null
            || !await ThreadAccess.CanSeeAsync(emailThreads, thread, visibleAccountIds, restrictToUserId, ct)
        )
            return null;

        return (email, thread);
    }
}

// Papelera de un correo entrante. Ajusta MessageCount del hilo.
public static class TrashIncomingMessageHandler
{
    public static async Task<Result> Handle(
        TrashIncomingMessageCommand command,
        IIncomingEmailRepository incomingEmails,
        IEmailThreadRepository emailThreads,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var loaded = await IncomingMessageAccess.LoadAsync(
            command.TenantId,
            command.MessageId,
            command.VisibleAccountIds,
            command.RestrictToUserId,
            incomingEmails,
            emailThreads,
            ct
        );
        if (loaded is not var (email, thread))
            return Result.Failure(IncomingMessageAccess.NotFound);

        if (email.SoftDelete(DateTime.UtcNow))
        {
            thread.DecrementMessageCount();
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
}

// Restaura un entrante desde la papelera. Ajusta MessageCount del hilo.
public static class RestoreIncomingMessageHandler
{
    public static async Task<Result> Handle(
        RestoreIncomingMessageCommand command,
        IIncomingEmailRepository incomingEmails,
        IEmailThreadRepository emailThreads,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var loaded = await IncomingMessageAccess.LoadAsync(
            command.TenantId,
            command.MessageId,
            command.VisibleAccountIds,
            command.RestrictToUserId,
            incomingEmails,
            emailThreads,
            ct
        );
        if (loaded is not var (email, thread))
            return Result.Failure(IncomingMessageAccess.NotFound);

        if (email.Restore())
        {
            thread.IncrementMessageCount();
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
}

// Borrado permanente. Solo desde la papelera (409 si no está borrado).
public static class PurgeIncomingMessageHandler
{
    public static async Task<Result> Handle(
        PurgeIncomingMessageCommand command,
        IIncomingEmailRepository incomingEmails,
        IEmailThreadRepository emailThreads,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var loaded = await IncomingMessageAccess.LoadAsync(
            command.TenantId,
            command.MessageId,
            command.VisibleAccountIds,
            command.RestrictToUserId,
            incomingEmails,
            emailThreads,
            ct
        );
        if (loaded is not var (email, _))
            return Result.Failure(IncomingMessageAccess.NotFound);

        if (!email.IsDeleted)
            return Result.Failure(
                new Error(
                    "IncomingEmail.NotTrashed",
                    "The message must be in the trash before deleting it permanently."
                )
            );

        incomingEmails.Remove(email);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
