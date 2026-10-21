using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.DataCollection.Engine;
using TaxVision.DataCollection.Engine.Formats;
using TaxVision.Tasks.ClientForms.Application.Common.Abstractions;
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
        IFormAuditLog audit,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var found = await requests.GetForCommandAsync(command.Context.TenantId, command.RequestId, ct);
        if (found.IsFailure || !FormRequestApplicationSupport.Matches(command.Context, found.Value))
            return Result.Failure<CapturedSensitiveAnswerResponse>(FormRequestErrors.NotFound);
        var version = await versions.GetByIdAsync(command.Context.TenantId, found.Value.FormTemplateVersionId, ct);
        if (version.IsFailure)
            return Result.Failure<CapturedSensitiveAnswerResponse>(FormRequestErrors.TemplateNotPublished);
        var definition = engine.ParseDefinition(version.Value.Definition.Json);
        var component = definition.IsSuccess
            ? FormRequestApplicationSupport.FindComponent(definition.Value, command.ComponentId)
            : null;
        if (component is null || component.Value.Component.Type != "taxId"
            || !AllowsKind(component.Value.Component.Props, command.Kind))
            return Result.Failure<CapturedSensitiveAnswerResponse>(FormRequestErrors.InvalidComponent);
        if (component.Value.Repeated && !await HasValidRow(found.Value, command, definition.Value, answersProtector, engine))
            return Result.Failure<CapturedSensitiveAnswerResponse>(FormRequestErrors.InvalidComponent);

        var engineKind = command.Kind switch
        {
            FormTaxIdentifierKind.Ssn => TaxIdentifierKind.Ssn,
            FormTaxIdentifierKind.Itin => TaxIdentifierKind.Itin,
            FormTaxIdentifierKind.Ein => TaxIdentifierKind.Ein,
            _ => (TaxIdentifierKind)(-1),
        };
        if (!TaxIdentifierFormat.TryNormalize(command.RawValue, engineKind, out var normalized))
            return Result.Failure<CapturedSensitiveAnswerResponse>(FormRequestErrors.InvalidTaxIdentifier);
        var protectedValue = sensitiveProtector.Protect(normalized);
        var value = ProtectedTaxIdentifier.Create(protectedValue.Cipher, protectedValue.KeyVersion, normalized[^4..]);
        if (value.IsFailure)
            return Result.Failure<CapturedSensitiveAnswerResponse>(value.Error);
        var address = FormAnswerAddress.Create(command.ComponentId, command.RowId);
        if (address.IsFailure)
            return Result.Failure<CapturedSensitiveAnswerResponse>(address.Error);
        var captured = found.Value.CaptureSensitiveAnswer(
            address.Value,
            command.Kind,
            value.Value,
            command.Context.Actor,
            command.NowUtc
        );
        if (captured.IsFailure)
            return Result.Failure<CapturedSensitiveAnswerResponse>(captured.Error);
        audit.RecordParticipant(command.Context.TenantId, command.RequestId, "sensitive.captured", command.Context.Actor);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(new CapturedSensitiveAnswerResponse(captured.Value.Id, normalized[^4..], command.Kind));
    }

    private static bool AllowsKind(System.Text.Json.JsonElement props, FormTaxIdentifierKind kind)
    {
        var expected = kind.ToString().ToLowerInvariant();
        return !props.TryGetProperty("kinds", out var kinds)
            || kinds.ValueKind != System.Text.Json.JsonValueKind.Array
            || kinds.EnumerateArray().Any(item => item.GetString() == expected);
    }

    private static Task<bool> HasValidRow(
        FormRequest request,
        CapturePortalSensitiveAnswerCommand command,
        TaxVision.DataCollection.Engine.Definitions.FormDefinition definition,
        IAnswersProtector protector,
        IFormEngine engine
    )
    {
        if (string.IsNullOrWhiteSpace(command.RowId) || request.Submission?.DraftAnswers is null)
            return Task.FromResult(false);
        var answers = engine.ParseAnswers(protector.Unprotect(request.Submission.DraftAnswers));
        return Task.FromResult(answers.IsSuccess
            && FormRequestApplicationSupport.HasRow(answers.Value, definition, command.ComponentId, command.RowId));
    }
}
