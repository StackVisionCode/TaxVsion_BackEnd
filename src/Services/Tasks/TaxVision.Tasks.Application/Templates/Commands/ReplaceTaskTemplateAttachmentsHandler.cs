using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Tasks.Application.Templates.Abstractions;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.Templates;

namespace TaxVision.Tasks.Application.Templates.Commands;

/// <param name="StepOrder">Sin paso, el archivo cuelga del primero: es donde el preparador empieza.</param>
public sealed record TaskTemplateAttachmentDraft(
    Guid FileId,
    string? DisplayName,
    string? ContentType,
    long SizeBytes,
    int? StepOrder
);

public sealed record ReplaceTaskTemplateAttachmentsCommand(
    Guid TenantId,
    Guid TemplateId,
    IReadOnlyList<TaskTemplateAttachmentDraft> Attachments
);

/// <summary>
/// Los archivos de referencia del guion: el checklist, el formulario en blanco. Cambiarlos no toca
/// las instancias ya creadas —el encargo en curso conserva el material con el que empezó—.
/// </summary>
public static class ReplaceTaskTemplateAttachmentsHandler
{
    public static async Task<Result<TaskTemplateResponse>> Handle(
        ReplaceTaskTemplateAttachmentsCommand command,
        ITaskTemplateRepository templates,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var found = await templates.GetByIdAsync(command.TenantId, command.TemplateId, ct);
        if (found.IsFailure)
            return Result.Failure<TaskTemplateResponse>(found.Error);

        var built = Build(command.Attachments);
        if (built.IsFailure)
            return Result.Failure<TaskTemplateResponse>(built.Error);

        var applied = found.Value.ReplaceAttachments(built.Value, DateTime.UtcNow);
        if (applied.IsFailure)
            return Result.Failure<TaskTemplateResponse>(applied.Error);

        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success(TaskTemplateResponse.From(found.Value));
    }

    private static Result<List<TaskTemplateAttachment>> Build(IReadOnlyList<TaskTemplateAttachmentDraft> drafts)
    {
        var attachments = new List<TaskTemplateAttachment>(drafts.Count);

        foreach (var draft in drafts)
        {
            var name = draft.DisplayName?.Trim();
            if (draft.FileId == Guid.Empty)
                return Result.Failure<List<TaskTemplateAttachment>>(TaskErrors.Attachment.FileRequired);

            if (string.IsNullOrWhiteSpace(name))
                return Result.Failure<List<TaskTemplateAttachment>>(TaskErrors.Attachment.DisplayNameRequired);

            if (name.Length > 260)
                return Result.Failure<List<TaskTemplateAttachment>>(TaskErrors.Attachment.DisplayNameTooLong);

            attachments.Add(
                TaskTemplateAttachment.Create(draft.FileId, name, draft.ContentType, draft.SizeBytes, draft.StepOrder)
            );
        }

        return Result.Success(attachments);
    }
}
