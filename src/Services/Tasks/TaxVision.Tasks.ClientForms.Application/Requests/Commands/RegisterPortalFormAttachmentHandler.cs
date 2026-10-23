using System.Text.Json;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.DataCollection.Engine;
using TaxVision.DataCollection.Engine.Definitions;
using TaxVision.Tasks.ClientForms.Application.Consents.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Requests.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Templates.Abstractions;
using TaxVision.Tasks.ClientForms.Domain.Requests;

namespace TaxVision.Tasks.ClientForms.Application.Requests.Commands;

public static class RegisterPortalFormAttachmentHandler
{
    public static async Task<Result<FormAttachmentResponse>> Handle(
        RegisterPortalFormAttachmentCommand command,
        IFormRequestRepository requests,
        IFormTemplateVersionRepository versions,
        IFormFileScanVerdictRepository verdicts,
        IFormEngine engine,
        IAnswersProtector protector,
        IFormConsentGate consentGate,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var found = await requests.GetForCommandAsync(command.Context.TenantId, command.RequestId, ct);
        if (found.IsFailure || !FormRequestApplicationSupport.Matches(command.Context, found.Value))
            return Result.Failure<FormAttachmentResponse>(FormRequestErrors.NotFound);
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
            return Result.Failure<FormAttachmentResponse>(component.Error);
        var allowed = EnsureFileAllowed(component.Value.Props, command);
        if (allowed.IsFailure)
            return Result.Failure<FormAttachmentResponse>(allowed.Error);
        var registered = await RegisterAsync(command, found.Value, component.Value.Props, verdicts, ct);
        if (registered.IsFailure)
            return Result.Failure<FormAttachmentResponse>(registered.Error);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(FormAttachmentResponse.From(registered.Value, found.Value));
    }

    // El componente debe ser un "file" de la versión fijada y, si está en un repeater, de una fila existente.
    private static async Task<Result<FormComponent>> LoadFileComponentAsync(
        RegisterPortalFormAttachmentCommand command,
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
        if (component.Value.Repeated && !HasValidRow(request, command, definition.Value, protector, engine))
            return Result.Failure<FormComponent>(FormRequestErrors.InvalidComponent);
        return Result.Success(component.Value.Component);
    }

    private static Result EnsureFileAllowed(JsonElement props, RegisterPortalFormAttachmentCommand command)
    {
        if (!AllowsType(props, command.DisplayName, command.ContentType))
            return Result.Failure(new Error("FormRequest.FileTypeNotAllowed", "The file type is not allowed."));
        return command.SizeBytes > ReadLong(props, "maxFileBytes", 25 * 1024 * 1024)
            ? Result.Failure(new Error("FormRequest.FileTooLarge", "The file is too large."))
            : Result.Success();
    }

    private static async Task<Result<FormAttachment>> RegisterAsync(
        RegisterPortalFormAttachmentCommand command,
        FormRequest request,
        JsonElement props,
        IFormFileScanVerdictRepository verdicts,
        CancellationToken ct
    )
    {
        var address = FormAnswerAddress.Create(command.ComponentId, command.RowId);
        if (address.IsFailure)
            return Result.Failure<FormAttachment>(address.Error);
        var known = await verdicts.GetAsync(command.FileId, ct);
        // Un veredicto proyectado de otro tenant no se aplica: el adjunto espera el suyo.
        var verdict = known is not null && known.TenantId == request.TenantId ? known.ToVerdict() : null;
        return request.RegisterAttachment(
            address.Value,
            command.FileId,
            command.DisplayName,
            command.ContentType,
            command.SizeBytes,
            ReadInt(props, "maxFiles", 5),
            20,
            verdict,
            command.NowUtc
        );
    }

    private static bool HasValidRow(
        FormRequest request,
        RegisterPortalFormAttachmentCommand command,
        FormDefinition definition,
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

    private static bool AllowsType(JsonElement props, string name, string contentType)
    {
        if (!props.TryGetProperty("allowedTypes", out var allowed) || allowed.ValueKind != JsonValueKind.Array)
            return true;
        var extension = Path.GetExtension(name).ToLowerInvariant();
        return allowed
            .EnumerateArray()
            .Any(item =>
                string.Equals(item.GetString(), contentType, StringComparison.OrdinalIgnoreCase)
                || string.Equals(item.GetString(), extension, StringComparison.OrdinalIgnoreCase)
            );
    }

    private static int ReadInt(JsonElement props, string name, int fallback) =>
        props.TryGetProperty(name, out var node) && node.TryGetInt32(out var value) ? value : fallback;

    private static long ReadLong(JsonElement props, string name, long fallback) =>
        props.TryGetProperty(name, out var node) && node.TryGetInt64(out var value) ? value : fallback;
}
