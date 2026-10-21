using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.DataCollection.Engine;
using TaxVision.DataCollection.Engine.Validation;
using TaxVision.Tasks.ClientForms.Application.Common.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Requests.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Templates.Abstractions;
using TaxVision.Tasks.ClientForms.Domain.Requests;

namespace TaxVision.Tasks.ClientForms.Application.Requests.Commands;

public sealed record SubmitPortalFormRequestCommand(
    ParticipantContext Context,
    Guid RequestId,
    byte[] RowVersion,
    DateTime NowUtc
);

public static class SubmitPortalFormRequestHandler
{
    public static async Task<Result<FormSubmitOutcome>> Handle(
        SubmitPortalFormRequestCommand command,
        IFormRequestRepository requests,
        IFormTemplateVersionRepository versions,
        IFormEngine engine,
        IAnswersProtector protector,
        IFormAuditLog audit,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var found = await requests.GetForCommandAsync(command.Context.TenantId, command.RequestId, ct);
        if (found.IsFailure || !FormRequestApplicationSupport.Matches(command.Context, found.Value))
            return Result.Failure<FormSubmitOutcome>(FormRequestErrors.NotFound);
        if (!FormRequestApplicationSupport.RowVersionMatches(found.Value, command.RowVersion))
            return Result.Failure<FormSubmitOutcome>(FormRequestErrors.ConcurrencyConflict);
        var version = await versions.GetByIdAsync(command.Context.TenantId, found.Value.FormTemplateVersionId, ct);
        if (version.IsFailure)
            return Result.Failure<FormSubmitOutcome>(FormRequestErrors.TemplateNotPublished);
        var definition = engine.ParseDefinition(version.Value.Definition.Json);
        var draft = found.Value.Submission?.DraftAnswers;
        var answers = draft is null ? null : engine.ParseAnswers(protector.Unprotect(draft));
        if (definition.IsFailure || answers is null || answers.IsFailure)
            return Result.Success(FormSubmitOutcome.Invalid([InvalidPayload()]));

        var visibility = engine.EvaluateVisibility(definition.Value, answers.Value);
        var sanitized = engine.RemoveHiddenAnswers(definition.Value, answers.Value, visibility);
        var issues = engine.ValidateAnswers(definition.Value, sanitized, AnswersValidationMode.Submit).Issues
            .Concat(FormRequestApplicationSupport.ValidateReferences(definition.Value, sanitized, found.Value, true))
            .ToArray();
        if (issues.Length > 0)
            return Result.Success(FormSubmitOutcome.Invalid(issues));
        var references = FormRequestApplicationSupport.References(definition.Value, sanitized);
        var submit = found.Value.Submit(
            protector.Protect(FormRequestApplicationSupport.Serialize(sanitized)),
            references.FileIds,
            references.SensitiveIds,
            command.Context.Actor,
            command.NowUtc
        );
        if (submit.IsFailure)
            return Result.Failure<FormSubmitOutcome>(submit.Error);
        audit.RecordParticipant(
            command.Context.TenantId,
            command.RequestId,
            "request.submitted",
            command.Context.Actor,
            $"{{\"revisionNumber\":{found.Value.CurrentRevisionNumber}}}"
        );
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(FormSubmitOutcome.Success(found.Value));
    }

    private static ValidationIssue InvalidPayload() => new(
        "answer.invalid_json",
        "/",
        null,
        null,
        "The saved answer payload is invalid."
    );
}
