using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Correspondence.Application.Abstractions;

namespace TaxVision.Correspondence.Application.Messages;

public sealed record TrashIncomingMessageCommand(Guid TenantId, Guid MessageId);

public sealed record RestoreIncomingMessageCommand(Guid TenantId, Guid MessageId);

public sealed record PurgeIncomingMessageCommand(Guid TenantId, Guid MessageId);

// Papelera de un correo entrante. Ajusta MessageCount del hilo.
public static class TrashIncomingMessageHandler
{
    public static async Task<Result> Handle(
        TrashIncomingMessageCommand command,
        IIncomingEmailRepository incomingEmails,
        IEmailThreadRepository emailThreads,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var email = await incomingEmails.GetByIdAsync(command.TenantId, command.MessageId, ct);
        if (email is null)
            return Result.Failure(new Error("IncomingEmail.NotFound", "The message was not found for this tenant."));

        if (email.SoftDelete(DateTime.UtcNow))
        {
            var thread = await emailThreads.GetByIdAsync(command.TenantId, email.EmailThreadId, ct);
            thread?.DecrementMessageCount();
        }

        await unitOfWork.SaveChangesAsync(ct);
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
        CancellationToken ct
    )
    {
        var email = await incomingEmails.GetByIdAsync(command.TenantId, command.MessageId, ct);
        if (email is null)
            return Result.Failure(new Error("IncomingEmail.NotFound", "The message was not found for this tenant."));

        if (email.Restore())
        {
            var thread = await emailThreads.GetByIdAsync(command.TenantId, email.EmailThreadId, ct);
            thread?.IncrementMessageCount();
        }

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}

// Borrado permanente. Solo desde la papelera (409 si no está borrado).
public static class PurgeIncomingMessageHandler
{
    public static async Task<Result> Handle(
        PurgeIncomingMessageCommand command,
        IIncomingEmailRepository incomingEmails,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var email = await incomingEmails.GetByIdAsync(command.TenantId, command.MessageId, ct);
        if (email is null)
            return Result.Failure(new Error("IncomingEmail.NotFound", "The message was not found for this tenant."));

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
