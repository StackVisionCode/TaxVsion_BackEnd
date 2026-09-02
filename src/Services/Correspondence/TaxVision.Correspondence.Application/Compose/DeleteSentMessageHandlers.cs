using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Correspondence.Application.Abstractions;

namespace TaxVision.Correspondence.Application.Compose;

public sealed record TrashSentMessageCommand(Guid TenantId, Guid MessageId);

public sealed record RestoreSentMessageCommand(Guid TenantId, Guid MessageId);

public sealed record PurgeSentMessageCommand(Guid TenantId, Guid MessageId);

// Papelera de un enviado. Solo un Sent es borrable (Draft.SoftDelete valida).
public static class TrashSentMessageHandler
{
    public static async Task<Result> Handle(
        TrashSentMessageCommand command,
        IDraftRepository drafts,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var draft = await drafts.GetByIdAsync(command.TenantId, command.MessageId, ct);
        if (draft is null)
            return Result.Failure(new Error("Draft.NotFound", "The message was not found for this tenant."));

        var result = draft.SoftDelete(DateTime.UtcNow);
        if (result.IsFailure)
            return result;

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}

public static class RestoreSentMessageHandler
{
    public static async Task<Result> Handle(
        RestoreSentMessageCommand command,
        IDraftRepository drafts,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var draft = await drafts.GetByIdAsync(command.TenantId, command.MessageId, ct);
        if (draft is null)
            return Result.Failure(new Error("Draft.NotFound", "The message was not found for this tenant."));

        draft.Restore();
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}

// Borrado permanente. Solo desde la papelera.
public static class PurgeSentMessageHandler
{
    public static async Task<Result> Handle(
        PurgeSentMessageCommand command,
        IDraftRepository drafts,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var draft = await drafts.GetByIdAsync(command.TenantId, command.MessageId, ct);
        if (draft is null)
            return Result.Failure(new Error("Draft.NotFound", "The message was not found for this tenant."));

        if (!draft.IsDeleted)
            return Result.Failure(
                new Error("Draft.NotTrashed", "The message must be in the trash before deleting it permanently.")
            );

        drafts.Remove(draft);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
