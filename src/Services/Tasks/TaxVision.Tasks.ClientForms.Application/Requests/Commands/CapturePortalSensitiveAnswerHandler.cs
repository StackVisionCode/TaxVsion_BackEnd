using System.Text.Json;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.DataCollection.Engine;
using TaxVision.DataCollection.Engine.Definitions;
using TaxVision.DataCollection.Engine.Formats;
using TaxVision.Tasks.ClientForms.Application.Common.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Consents.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Requests.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Templates.Abstractions;
using TaxVision.Tasks.ClientForms.Domain.Requests;

namespace TaxVision.Tasks.ClientForms.Application.Requests.Commands;

public sealed record CapturePortalSensitiveAnswerCommand(
    ParticipantContext Context,
    Guid RequestId,
    string ComponentId,
    string? RowId,
    FormTaxIdentifierKind Kind,
    string RawValue,
    DateTime NowUtc
);

public static class CapturePortalSensitiveAnswerHandler
{
    public static async Task<Result<CapturedSensitiveAnswerResponse>> Handle(
        CapturePortalSensitiveAnswerCommand command,
        IFormRequestRepository requests,
        IFormTemplateVersionRepository versions,
        IFormEngine engine,
        IAnswersProtector answersProtector,
        ISensitiveValueProtector sensitiveProtector,
        IFormConsentGate consentGate,
        IFormAuditLog audit,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var found = await requests.GetForCommandAsync(command.Context.TenantId, command.RequestId, ct);
        if (found.IsFailure || !FormRequestApplicationSupport.Matches(command.Context, found.Value))
            return Result.Failure<CapturedSensitiveAnswerResponse>(FormRequestErrors.NotFound);
        var target = await EnsureTaxIdComponentAsync(
            command,
            found.Value,
            versions,
            engine,
            answersProtector,
            consentGate,
            ct
        );
        if (target.IsFailure)
            return Result.Failure<CapturedSensitiveAnswerResponse>(target.Error);
        var captured = Capture(command, found.Value, sensitiveProtector);
        if (captured.IsFailure)
            return Result.Failure<CapturedSensitiveAnswerResponse>(captured.Error);
        audit.RecordParticipant(
            command.Context.TenantId,
            command.RequestId,
            "sensitive.captured",
            command.Context.Actor
        );
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(Response(command, found.Value, captured.Value));
    }

    // El componente es un "taxId" de la versión fijada que admite ese tipo y, si se repite, de una fila existente.
    private static async Task<Result> EnsureTaxIdComponentAsync(
        CapturePortalSensitiveAnswerCommand command,
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
            return Result.Failure(FormRequestErrors.TemplateNotPublished);
        var consent = await consentGate.EnsureBeforeCollectionAsync(
            command.Context,
            request,
            definition.Value,
            null,
            true,
            ct
        );
        if (consent.IsFailure)
            return consent;
        var component = FormRequestApplicationSupport.FindComponent(definition.Value, command.ComponentId);
        if (
            component is null
            || component.Value.Component.Type != "taxId"
            || !AllowsKind(component.Value.Component.Props, command.Kind)
        )
            return Result.Failure(FormRequestErrors.InvalidComponent);
        return component.Value.Repeated && !HasValidRow(request, command, definition.Value, protector, engine)
            ? Result.Failure(FormRequestErrors.InvalidComponent)
            : Result.Success();
    }

    private static Result<ProtectedTaxIdentifier> Protect(
        CapturePortalSensitiveAnswerCommand command,
        ISensitiveValueProtector protector
    )
    {
        var engineKind = command.Kind switch
        {
            FormTaxIdentifierKind.Ssn => TaxIdentifierKind.Ssn,
            FormTaxIdentifierKind.Itin => TaxIdentifierKind.Itin,
            FormTaxIdentifierKind.Ein => TaxIdentifierKind.Ein,
            _ => (TaxIdentifierKind)(-1),
        };
        if (!TaxIdentifierFormat.TryNormalize(command.RawValue, engineKind, out var normalized))
            return Result.Failure<ProtectedTaxIdentifier>(FormRequestErrors.InvalidTaxIdentifier);
        var protectedValue = protector.Protect(normalized);
        return ProtectedTaxIdentifier.Create(protectedValue.Cipher, protectedValue.KeyVersion, normalized[^4..]);
    }

    // Normaliza, cifra y reemplaza la respuesta activa de ese campo; el valor en claro no sale de acá.
    private static Result<FormSensitiveAnswer> Capture(
        CapturePortalSensitiveAnswerCommand command,
        FormRequest request,
        ISensitiveValueProtector protector
    )
    {
        var value = Protect(command, protector);
        var address = FormAnswerAddress.Create(command.ComponentId, command.RowId);
        if (value.IsFailure || address.IsFailure)
            return Result.Failure<FormSensitiveAnswer>(value.IsFailure ? value.Error : address.Error);
        return request.CaptureSensitiveAnswer(
            address.Value,
            command.Kind,
            value.Value,
            command.Context.Actor,
            command.NowUtc
        );
    }

    private static CapturedSensitiveAnswerResponse Response(
        CapturePortalSensitiveAnswerCommand command,
        FormRequest request,
        FormSensitiveAnswer answer
    ) => new(answer.Id, answer.Protected.Last4, command.Kind, Convert.ToBase64String(request.RowVersion));

    private static bool AllowsKind(JsonElement props, FormTaxIdentifierKind kind)
    {
        var expected = kind.ToString().ToLowerInvariant();
        return !props.TryGetProperty("kinds", out var kinds)
            || kinds.ValueKind != JsonValueKind.Array
            || kinds.EnumerateArray().Any(item => item.GetString() == expected);
    }

    private static bool HasValidRow(
        FormRequest request,
        CapturePortalSensitiveAnswerCommand command,
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
}
