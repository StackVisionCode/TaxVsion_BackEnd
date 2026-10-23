using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Options;
using TaxVision.DataCollection.Engine;
using TaxVision.DataCollection.Engine.Validation;
using TaxVision.Tasks.ClientForms.Application.Common;
using TaxVision.Tasks.ClientForms.Application.Common.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Consents;
using TaxVision.Tasks.ClientForms.Application.Consents.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Participants.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Requests.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Templates.Abstractions;
using TaxVision.Tasks.ClientForms.Domain.Participants;
using TaxVision.Tasks.ClientForms.Domain.Requests;
using Wolverine;

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
        IClientFormsCustomerReader customers,
        IFormIntakeLinkRepository intakeLinks,
        IFormConsentGate consentGate,
        IFormConsentRecordRepository consentRecords,
        IOptions<ClientFormsOptions> options,
        IFormAuditLog audit,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var found = await LoadAsync(command, requests, ct);
        if (found.IsFailure)
            return Result.Failure<FormSubmitOutcome>(found.Error);
        var check = await CheckAnswersAsync(command, found.Value, versions, engine, protector, consentGate, ct);
        if (check.IsFailure)
            return Result.Failure<FormSubmitOutcome>(check.Error);
        if (check.Value.Issues.Count > 0)
            return Result.Success(FormSubmitOutcome.Invalid(check.Value.Issues));
        var intakeLink = await IntakeLinkAsync(command, found.Value, requests, intakeLinks, options.Value, ct);
        if (intakeLink.IsFailure)
            return Result.Failure<FormSubmitOutcome>(intakeLink.Error);
        var submit = await SubmitAsync(command, found.Value, check.Value, protector, consentRecords, ct);
        if (submit.IsFailure)
            return Result.Failure<FormSubmitOutcome>(submit.Error);
        if (intakeLink.Value?.RegisterSubmission(command.NowUtc).IsFailure == true)
            return Result.Failure<FormSubmitOutcome>(FormParticipantErrors.IntakeLimitReached);
        RecordSubmitted(command, found.Value, audit);
        await bus.PublishAsync(
            await FormRequestIntegrationEventFactory.SubmittedAsync(
                found.Value,
                submit.Value,
                customers,
                correlation.CorrelationId,
                ct
            )
        );
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(FormSubmitOutcome.Success(found.Value));
    }

    private static async Task<Result<FormRequest>> LoadAsync(
        SubmitPortalFormRequestCommand command,
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

    private static async Task<Result<FormSubmitCheck>> CheckAnswersAsync(
        SubmitPortalFormRequestCommand command,
        FormRequest request,
        IFormTemplateVersionRepository versions,
        IFormEngine engine,
        IAnswersProtector protector,
        IFormConsentGate consentGate,
        CancellationToken ct
    )
    {
        var version = await versions.GetByIdAsync(command.Context.TenantId, request.FormTemplateVersionId, ct);
        if (version.IsFailure)
            return Result.Failure<FormSubmitCheck>(FormRequestErrors.TemplateNotPublished);
        var definition = engine.ParseDefinition(version.Value.Definition.Json);
        var draft = request.Submission?.DraftAnswers;
        var answers = draft is null ? null : engine.ParseAnswers(protector.Unprotect(draft));
        if (definition.IsFailure || answers is null || answers.IsFailure)
            return Result.Success(FormSubmitCheck.Invalid([InvalidPayload()]));
        var visibility = engine.EvaluateVisibility(definition.Value, answers.Value);
        var consent = await consentGate.EnsureBeforeSubmitAsync(
            command.Context,
            request,
            definition.Value,
            visibility,
            ct
        );
        if (consent.IsFailure)
            return Result.Failure<FormSubmitCheck>(consent.Error);
        var sanitized = engine.RemoveHiddenAnswers(definition.Value, answers.Value, visibility);
        var issues = engine
            .ValidateAnswers(definition.Value, sanitized, AnswersValidationMode.Submit)
            .Issues.Concat(FormRequestApplicationSupport.ValidateReferences(definition.Value, sanitized, request, true))
            .ToArray();
        if (issues.Length > 0)
            return Result.Success(FormSubmitCheck.Invalid(issues));
        var references = FormRequestApplicationSupport.References(definition.Value, sanitized);
        var canonical = FormSensitiveAnswerCanonicalizer.Apply(definition.Value, sanitized, request);
        return Result.Success(new FormSubmitCheck(canonical, references.FileIds, references.SensitiveIds, []));
    }

    // Solo el primer envío de un enlace de captación consume el cupo diario del tenant y del enlace.
    private static async Task<Result<FormIntakeLink?>> IntakeLinkAsync(
        SubmitPortalFormRequestCommand command,
        FormRequest request,
        IFormRequestRepository requests,
        IFormIntakeLinkRepository intakeLinks,
        ClientFormsOptions options,
        CancellationToken ct
    )
    {
        if (request.Origin != FormRequestOrigin.IntakeLink || request.CurrentRevisionNumber != 0)
            return Result.Success<FormIntakeLink?>(null);
        var submittedToday = await requests.CountIntakeSubmissionsAsync(
            command.Context.TenantId,
            command.NowUtc.Date,
            ct
        );
        if (submittedToday >= options.External.MaxIntakeSubmissionsPerTenantPerDay)
            return Result.Failure<FormIntakeLink?>(FormParticipantErrors.IntakeLimitReached);
        var intakeLink = request.IntakeLinkId.HasValue
            ? await intakeLinks.GetAsync(command.Context.TenantId, request.IntakeLinkId.Value, ct)
            : null;
        return intakeLink is null
            ? Result.Failure<FormIntakeLink?>(FormParticipantErrors.IntakeLinkNotFound)
            : Result.Success<FormIntakeLink?>(intakeLink);
    }

    private static async Task<Result<FormSubmissionRevision>> SubmitAsync(
        SubmitPortalFormRequestCommand command,
        FormRequest request,
        FormSubmitCheck check,
        IAnswersProtector protector,
        IFormConsentRecordRepository consentRecords,
        CancellationToken ct
    )
    {
        var submit = request.Submit(
            protector.Protect(FormRequestApplicationSupport.Serialize(check.Sanitized!)),
            check.FileIds,
            check.SensitiveIds,
            command.Context.Actor,
            command.NowUtc
        );
        if (submit.IsFailure)
            return submit;
        var bound = await FormConsentRevisionBinder.BindPendingAsync(
            command.Context,
            request.Id,
            submit.Value.RevisionNumber,
            consentRecords,
            ct
        );
        return bound.IsFailure ? Result.Failure<FormSubmissionRevision>(bound.Error) : submit;
    }

    private static void RecordSubmitted(
        SubmitPortalFormRequestCommand command,
        FormRequest request,
        IFormAuditLog audit
    ) =>
        audit.RecordParticipant(
            command.Context.TenantId,
            command.RequestId,
            "request.submitted",
            command.Context.Actor,
            $"{{\"revisionNumber\":{request.CurrentRevisionNumber}}}"
        );

    private static ValidationIssue InvalidPayload() =>
        new("answer.invalid_json", "/", null, null, "The saved answer payload is invalid.");
}
