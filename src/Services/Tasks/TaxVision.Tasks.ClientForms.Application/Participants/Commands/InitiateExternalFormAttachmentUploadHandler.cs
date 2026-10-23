using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Options;
using TaxVision.DataCollection.Engine;
using TaxVision.DataCollection.Engine.Definitions;
using TaxVision.Tasks.ClientForms.Application.Common;
using TaxVision.Tasks.ClientForms.Application.Consents.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Participants.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Requests;
using TaxVision.Tasks.ClientForms.Application.Requests.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Templates.Abstractions;
using TaxVision.Tasks.ClientForms.Domain.Requests;

namespace TaxVision.Tasks.ClientForms.Application.Participants.Commands;

public static class InitiateExternalFormAttachmentUploadHandler
{
    public static async Task<Result<ExternalFormAttachmentUploadResponse>> Handle(
        InitiateExternalFormAttachmentUploadCommand command,
        IFormRequestRepository requests,
        IFormTemplateVersionRepository versions,
        IFormEngine engine,
        IAnswersProtector protector,
        IFormConsentGate consentGate,
        IFormExternalUploadClient uploads,
        IOptions<ClientFormsOptions> options,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var requestId = command.Context.BoundRequestId ?? Guid.Empty;
        var found = await requests.GetForCommandAsync(command.Context.TenantId, requestId, ct);
        if (found.IsFailure || !FormRequestApplicationSupport.Matches(command.Context, found.Value))
            return Failure(FormRequestErrors.NotFound);
        var component = await LoadFileComponentAsync(
            command,
            found.Value,
            versions,
            engine,
            protector,
            consentGate,
            ct
        );
        if (component.IsFailure)
            return Failure(component.Error);
        var allowed = EnsureWithinLimits(command, found.Value, component.Value, options.Value.Limits);
        if (allowed.IsFailure)
            return Failure(allowed.Error);
        var initiated = await uploads.InitiateAsync(UploadRequest(command, found.Value), ct);
        if (initiated.IsFailure)
            return Failure(initiated.Error);
        var registered = Register(command, found.Value, component.Value, initiated.Value, options.Value.Limits);
        if (registered.IsFailure)
            return Failure(registered.Error);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(Response(found.Value, registered.Value, initiated.Value));
    }

    // El componente es un "file" de la versión fijada y, si está en un repeater, de una fila existente.
    private static async Task<Result<FormComponent>> LoadFileComponentAsync(
        InitiateExternalFormAttachmentUploadCommand command,
        FormRequest request,
        IFormTemplateVersionRepository versions,
        IFormEngine engine,
        IAnswersProtector protector,
        IFormConsentGate consentGate,
        CancellationToken ct
    )
    {
        var version = await versions.GetByIdAsync(command.Context.TenantId, request.FormTemplateVersionId, ct);
        var definition = version.IsFailure ? null : engine.ParseDefinition(version.Value.Definition.Json);
        if (definition is null || definition.IsFailure)
            return Result.Failure<FormComponent>(FormRequestErrors.TemplateNotPublished);
        var consent = await consentGate.EnsureBeforeCollectionAsync(
            command.Context,
            request,
            definition.Value,
            null,
            true,
            ct
        );
        if (consent.IsFailure)
            return Result.Failure<FormComponent>(consent.Error);
        var component = FormRequestApplicationSupport.FindComponent(definition.Value, command.ComponentId);
        if (component is null || component.Value.Component.Type != "file")
            return Result.Failure<FormComponent>(FormRequestErrors.InvalidComponent);
        if (component.Value.Repeated && !HasValidRow(command, request, definition.Value, engine, protector))
            return Result.Failure<FormComponent>(FormRequestErrors.InvalidComponent);
        return Result.Success(component.Value.Component);
    }

    // En un componente repetido, la fila debe existir en el borrador guardado.
    private static bool HasValidRow(
        InitiateExternalFormAttachmentUploadCommand command,
        FormRequest request,
        FormDefinition definition,
        IFormEngine engine,
        IAnswersProtector protector
    )
    {
        var draft = request.Submission?.DraftAnswers is { } protectedAnswers
            ? protector.Unprotect(protectedAnswers)
            : null;
        return ExternalFormAttachmentSupport.HasValidRow(
            request,
            command.ComponentId,
            command.RowId,
            definition,
            draft,
            engine
        );
    }

    private static Result EnsureWithinLimits(
        InitiateExternalFormAttachmentUploadCommand command,
        FormRequest request,
        FormComponent component,
        DataCollectionLimits limits
    )
    {
        var maxBytes = Math.Min(
            ExternalFormAttachmentSupport.ReadMaxBytes(component.Props, limits.MaxFileBytes),
            limits.MaxFileBytes
        );
        var usedBytes =
            request
                .Submission?.Attachments.Where(item => item.Status != FormAttachmentStatus.Detached)
                .Sum(item => item.SizeBytes)
            ?? 0;
        if (
            command.SizeBytes <= 0
            || command.SizeBytes > maxBytes
            || usedBytes + command.SizeBytes > limits.MaxSubmissionTotalBytes
        )
            return Result.Failure(new Error("FormRequest.FileTooLarge", "The file is too large."));
        return ExternalFormAttachmentSupport.AllowsType(component.Props, command.OriginalName, command.ContentType)
            ? Result.Success()
            : Result.Failure(new Error("FormRequest.FileTypeNotAllowed", "The file type is not allowed."));
    }

    // Vinculada a un cliente, el archivo nace del cliente; si no, del participante.
    private static ExternalFormUploadRequest UploadRequest(
        InitiateExternalFormAttachmentUploadCommand command,
        FormRequest request
    ) =>
        new(
            command.Context.TenantId,
            request.EffectiveCustomerId.HasValue ? "Customer" : "FormParticipant",
            request.EffectiveCustomerId ?? command.Context.ParticipantId!.Value,
            command.OriginalName,
            command.ContentType,
            command.SizeBytes
        );

    private static Result<FormAttachment> Register(
        InitiateExternalFormAttachmentUploadCommand command,
        FormRequest request,
        FormComponent component,
        ExternalFormUploadResponse initiated,
        DataCollectionLimits limits
    )
    {
        var address = FormAnswerAddress.Create(command.ComponentId, command.RowId);
        if (address.IsFailure)
            return Result.Failure<FormAttachment>(address.Error);
        return request.RegisterAttachment(
            address.Value,
            initiated.FileId,
            command.OriginalName,
            command.ContentType,
            command.SizeBytes,
            ExternalFormAttachmentSupport.ReadMaxFiles(component.Props, limits.MaxFilesPerField),
            limits.MaxFilesPerSubmission,
            null,
            command.NowUtc
        );
    }

    private static ExternalFormAttachmentUploadResponse Response(
        FormRequest request,
        FormAttachment attachment,
        ExternalFormUploadResponse initiated
    ) =>
        new(
            initiated.FileId,
            attachment.Id,
            new ExternalFormPresignedPost(initiated.UploadUrl, initiated.FormData, initiated.ExpiresAtUtc),
            Convert.ToBase64String(request.RowVersion)
        );

    private static Result<ExternalFormAttachmentUploadResponse> Failure(Error error) =>
        Result.Failure<ExternalFormAttachmentUploadResponse>(error);
}
