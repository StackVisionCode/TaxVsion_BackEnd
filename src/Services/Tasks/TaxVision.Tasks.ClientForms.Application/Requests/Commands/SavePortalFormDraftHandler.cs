using System.Diagnostics;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.DataCollection.Engine;
using TaxVision.DataCollection.Engine.Answers;
using TaxVision.DataCollection.Engine.Definitions;
using TaxVision.DataCollection.Engine.Validation;
using TaxVision.Tasks.ClientForms.Application.Common;
using TaxVision.Tasks.ClientForms.Application.Common.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Consents.Abstractions;
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
        IFormConsentGate consentGate,
        IUnitOfWork unitOfWork,
        IClientFormsMetrics metrics,
        CancellationToken ct
    )
    {
        var started = Stopwatch.GetTimestamp();
        var outcome = ClientFormsMetricValues.Error;
        try
        {
            var result = await SaveDraftAsync(
                command,
                requests,
                versions,
                engine,
                protector,
                consentGate,
                unitOfWork,
                ct
            );
            outcome = OutcomeOf(result);
            return result;
        }
        catch (ConflictException)
        {
            outcome = ClientFormsMetricValues.Conflict;
            throw;
        }
        finally
        {
            metrics.RecordDraftSave(outcome, Stopwatch.GetElapsedTime(started));
        }
    }

    // Un rechazo de negocio cuenta como inválido; "error" queda para lo que reventó.
    private static string OutcomeOf(Result<FormDraftSaveOutcome> result)
    {
        if (result.IsSuccess)
            return result.Value.Saved ? ClientFormsMetricValues.Ok : ClientFormsMetricValues.Invalid;
        return result.Error.Code == FormRequestErrors.ConcurrencyConflict.Code
            ? ClientFormsMetricValues.Conflict
            : ClientFormsMetricValues.Invalid;
    }

    private static async Task<Result<FormDraftSaveOutcome>> SaveDraftAsync(
        SavePortalFormDraftCommand command,
        IFormRequestRepository requests,
        IFormTemplateVersionRepository versions,
        IFormEngine engine,
        IAnswersProtector protector,
        IFormConsentGate consentGate,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var found = await LoadAsync(command, requests, ct);
        if (found.IsFailure)
            return Result.Failure<FormDraftSaveOutcome>(found.Error);
        var version = await versions.GetByIdAsync(command.Context.TenantId, found.Value.FormTemplateVersionId, ct);
        if (version.IsFailure)
            return Result.Failure<FormDraftSaveOutcome>(FormRequestErrors.TemplateNotPublished);
        var definition = engine.ParseDefinition(version.Value.Definition.Json);
        var answers = engine.ParseAnswers(command.AnswersJson);
        if (definition.IsFailure || answers.IsFailure)
            return Result.Success(FormDraftSaveOutcome.Invalid([InvalidPayload()]));
        var consent = await consentGate.EnsureBeforeCollectionAsync(
            command.Context,
            found.Value,
            definition.Value,
            answers.Value,
            false,
            ct
        );
        if (consent.IsFailure)
            return Result.Failure<FormDraftSaveOutcome>(consent.Error);
        return await SaveAsync(
            command,
            found.Value,
            definition.Value,
            answers.Value,
            engine,
            protector,
            unitOfWork,
            ct
        );
    }

    private static async Task<Result<FormRequest>> LoadAsync(
        SavePortalFormDraftCommand command,
        IFormRequestRepository requests,
        CancellationToken ct
    )
    {
        var found = await requests.GetForCommandAsync(command.Context.TenantId, command.RequestId, ct);
        if (found.IsFailure || !FormRequestApplicationSupport.Matches(command.Context, found.Value))
            return Result.Failure<FormRequest>(FormRequestErrors.NotFound);
        return FormRequestApplicationSupport.RowVersionMatches(found.Value, command.RowVersion)
            ? found
            : Result.Failure<FormRequest>(FormRequestErrors.ConcurrencyConflict);
    }

    // Valida tipos y referencias; solo un borrador limpio se cifra y se guarda.
    private static async Task<Result<FormDraftSaveOutcome>> SaveAsync(
        SavePortalFormDraftCommand command,
        FormRequest request,
        FormDefinition definition,
        FormAnswers answers,
        IFormEngine engine,
        IAnswersProtector protector,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var issues = engine
            .ValidateAnswers(definition, answers, AnswersValidationMode.Draft)
            .Issues.Concat(FormRequestApplicationSupport.ValidateReferences(definition, answers, request, false))
            .ToArray();
        if (issues.Length > 0)
            return Result.Success(FormDraftSaveOutcome.Invalid(issues));
        var visibility = engine.EvaluateVisibility(definition, answers);
        var progress = engine.CalculateProgress(definition, answers, visibility);
        var canonical = FormSensitiveAnswerCanonicalizer.Apply(definition, answers, request);
        var save = request.SaveDraft(
            protector.Protect(FormRequestApplicationSupport.Serialize(canonical)),
            (int)progress.Percentage,
            command.Context.Actor,
            command.NowUtc
        );
        if (save.IsFailure)
            return Result.Failure<FormDraftSaveOutcome>(save.Error);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(FormDraftSaveOutcome.Success(request, command.NowUtc));
    }

    private static ValidationIssue InvalidPayload() =>
        new("answer.invalid_json", "/", null, null, "The answer payload is invalid.");
}
