using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.DataCollection.Engine;
using TaxVision.DataCollection.Engine.Validation;
using TaxVision.Tasks.ClientForms.Application.Requests.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Templates.Abstractions;
using TaxVision.Tasks.ClientForms.Domain.Requests;

namespace TaxVision.Tasks.ClientForms.Application.Requests.Commands;

public sealed record SavePortalFormDraftCommand(
    ParticipantContext Context,
    Guid RequestId,
    string AnswersJson,
    byte[] RowVersion,
    DateTime NowUtc
);

public static class SavePortalFormDraftHandler
{
    public static async Task<Result<FormDraftSaveOutcome>> Handle(
        SavePortalFormDraftCommand command,
        IFormRequestRepository requests,
        IFormTemplateVersionRepository versions,
        IFormEngine engine,
        IAnswersProtector protector,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var found = await requests.GetForCommandAsync(command.Context.TenantId, command.RequestId, ct);
        if (found.IsFailure || !FormRequestApplicationSupport.Matches(command.Context, found.Value))
            return Result.Failure<FormDraftSaveOutcome>(FormRequestErrors.NotFound);
        if (!FormRequestApplicationSupport.RowVersionMatches(found.Value, command.RowVersion))
            return Result.Failure<FormDraftSaveOutcome>(FormRequestErrors.ConcurrencyConflict);
        var version = await versions.GetByIdAsync(command.Context.TenantId, found.Value.FormTemplateVersionId, ct);
        if (version.IsFailure)
            return Result.Failure<FormDraftSaveOutcome>(FormRequestErrors.TemplateNotPublished);
        var definition = engine.ParseDefinition(version.Value.Definition.Json);
        var answers = engine.ParseAnswers(command.AnswersJson);
        if (definition.IsFailure || answers.IsFailure)
            return Result.Success(FormDraftSaveOutcome.Invalid([InvalidPayload()]));

        var visibility = engine.EvaluateVisibility(definition.Value, answers.Value);
        var issues = engine.ValidateAnswers(definition.Value, answers.Value, AnswersValidationMode.Draft).Issues
            .Concat(FormRequestApplicationSupport.ValidateReferences(definition.Value, answers.Value, found.Value, false))
            .ToArray();
        if (issues.Length > 0)
            return Result.Success(FormDraftSaveOutcome.Invalid(issues));
        var progress = engine.CalculateProgress(definition.Value, answers.Value, visibility);
        var save = found.Value.SaveDraft(
            protector.Protect(FormRequestApplicationSupport.Serialize(answers.Value)),
            (int)progress.Percentage,
            command.Context.Actor,
            command.NowUtc
        );
        if (save.IsFailure)
            return Result.Failure<FormDraftSaveOutcome>(save.Error);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(FormDraftSaveOutcome.Success(found.Value, command.NowUtc));
    }

    private static ValidationIssue InvalidPayload() => new(
        "answer.invalid_json",
        "/",
        null,
        null,
        "The answer payload is invalid."
    );
}
