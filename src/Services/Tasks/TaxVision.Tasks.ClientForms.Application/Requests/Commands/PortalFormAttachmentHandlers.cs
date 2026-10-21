using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.DataCollection.Engine;
using TaxVision.Tasks.ClientForms.Application.Requests.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Templates.Abstractions;
using TaxVision.Tasks.ClientForms.Domain.Requests;

namespace TaxVision.Tasks.ClientForms.Application.Requests.Commands;

public sealed record RegisterPortalFormAttachmentCommand(
    ParticipantContext Context,
    Guid RequestId,
    string ComponentId,
    string? RowId,
    Guid FileId,
    string DisplayName,
    string ContentType,
    long SizeBytes,
    DateTime NowUtc
);

public sealed record FormAttachmentResponse(
    Guid Id,
    Guid FileId,
    string ComponentId,
    string? RowId,
    string DisplayName,
    FormAttachmentStatus Status,
    string? RejectionCode
)
{
    public static FormAttachmentResponse From(FormAttachment value) => new(
        value.Id,
        value.FileId,
        value.ComponentId,
        value.RowId,
        value.DisplayName,
        value.Status,
        value.RejectionCode
    );
}

public static class RegisterPortalFormAttachmentHandler
{
    public static async Task<Result<FormAttachmentResponse>> Handle(
        RegisterPortalFormAttachmentCommand command,
        IFormRequestRepository requests,
        IFormTemplateVersionRepository versions,
        IFormFileScanVerdictRepository verdicts,
        IFormEngine engine,
        IAnswersProtector protector,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var found = await requests.GetForCommandAsync(command.Context.TenantId, command.RequestId, ct);
        if (found.IsFailure || !FormRequestApplicationSupport.Matches(command.Context, found.Value))
            return Result.Failure<FormAttachmentResponse>(FormRequestErrors.NotFound);
        var version = await versions.GetByIdAsync(command.Context.TenantId, found.Value.FormTemplateVersionId, ct);
        if (version.IsFailure)
            return Result.Failure<FormAttachmentResponse>(FormRequestErrors.TemplateNotPublished);
        var definition = engine.ParseDefinition(version.Value.Definition.Json);
        var component = definition.IsSuccess
            ? FormRequestApplicationSupport.FindComponent(definition.Value, command.ComponentId)
            : null;
        if (component is null || component.Value.Component.Type != "file")
            return Result.Failure<FormAttachmentResponse>(FormRequestErrors.InvalidComponent);
        if (component.Value.Repeated && !HasValidRow(found.Value, command, definition.Value, protector, engine))
            return Result.Failure<FormAttachmentResponse>(FormRequestErrors.InvalidComponent);
        if (!AllowsType(component.Value.Component.Props, command.DisplayName, command.ContentType))
            return Result.Failure<FormAttachmentResponse>(new Error("FormRequest.FileTypeNotAllowed", "The file type is not allowed."));
        var maxBytes = ReadLong(component.Value.Component.Props, "maxFileBytes", 25 * 1024 * 1024);
        if (command.SizeBytes > maxBytes)
            return Result.Failure<FormAttachmentResponse>(new Error("FormRequest.FileTooLarge", "The file is too large."));
        var address = FormAnswerAddress.Create(command.ComponentId, command.RowId);
        if (address.IsFailure)
            return Result.Failure<FormAttachmentResponse>(address.Error);
        var known = await verdicts.GetAsync(command.FileId, ct);
        var registered = found.Value.RegisterAttachment(
            address.Value,
            command.FileId,
            command.DisplayName,
            command.ContentType,
            command.SizeBytes,
            ReadInt(component.Value.Component.Props, "maxFiles", 5),
            20,
            known?.ToVerdict(),
            command.NowUtc
        );
        if (registered.IsFailure)
            return Result.Failure<FormAttachmentResponse>(registered.Error);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(FormAttachmentResponse.From(registered.Value));
    }

    private static bool HasValidRow(
        FormRequest request,
        RegisterPortalFormAttachmentCommand command,
        TaxVision.DataCollection.Engine.Definitions.FormDefinition definition,
        IAnswersProtector protector,
        IFormEngine engine
    )
    {
        if (string.IsNullOrWhiteSpace(command.RowId) || request.Submission?.DraftAnswers is null)
            return false;
        var answers = engine.ParseAnswers(protector.Unprotect(request.Submission.DraftAnswers));
        return answers.IsSuccess
            && FormRequestApplicationSupport.HasRow(answers.Value, definition, command.ComponentId, command.RowId);
    }

    private static bool AllowsType(System.Text.Json.JsonElement props, string name, string contentType)
    {
        if (!props.TryGetProperty("allowedTypes", out var allowed)
            || allowed.ValueKind != System.Text.Json.JsonValueKind.Array)
            return true;
        var extension = Path.GetExtension(name).ToLowerInvariant();
        return allowed.EnumerateArray().Any(item =>
            string.Equals(item.GetString(), contentType, StringComparison.OrdinalIgnoreCase)
            || string.Equals(item.GetString(), extension, StringComparison.OrdinalIgnoreCase)
        );
    }

    private static int ReadInt(System.Text.Json.JsonElement props, string name, int fallback) =>
        props.TryGetProperty(name, out var node) && node.TryGetInt32(out var value) ? value : fallback;

    private static long ReadLong(System.Text.Json.JsonElement props, string name, long fallback) =>
        props.TryGetProperty(name, out var node) && node.TryGetInt64(out var value) ? value : fallback;
}

public sealed record DetachPortalFormAttachmentCommand(
    ParticipantContext Context,
    Guid RequestId,
    Guid AttachmentId,
    DateTime NowUtc
);

public static class DetachPortalFormAttachmentHandler
{
    public static async Task<Result> Handle(
        DetachPortalFormAttachmentCommand command,
        IFormRequestRepository requests,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var found = await requests.GetForCommandAsync(command.Context.TenantId, command.RequestId, ct);
        if (found.IsFailure || !FormRequestApplicationSupport.Matches(command.Context, found.Value))
            return Result.Failure(FormRequestErrors.NotFound);
        var detached = found.Value.DetachAttachment(command.AttachmentId, command.NowUtc);
        if (detached.IsFailure)
            return detached;
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
