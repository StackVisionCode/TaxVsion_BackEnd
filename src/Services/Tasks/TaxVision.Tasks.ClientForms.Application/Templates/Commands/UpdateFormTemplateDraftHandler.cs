using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.DataCollection.Engine;
using TaxVision.DataCollection.Engine.Definitions;
using TaxVision.Tasks.ClientForms.Application.Common;
using TaxVision.Tasks.ClientForms.Application.Templates.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Templates.Responses;

namespace TaxVision.Tasks.ClientForms.Application.Templates.Commands;

public sealed record UpdateFormTemplateDraftCommand(
    Guid TenantId,
    Guid ByUserId,
    Guid TemplateId,
    string DefinitionJson,
    byte[] RowVersion,
    bool CanConfigureActions
);

public static class UpdateFormTemplateDraftHandler
{
    public static async Task<Result<UpdateFormTemplateDraftOutcome>> Handle(
        UpdateFormTemplateDraftCommand command,
        IFormEngine engine,
        IFormTemplateRepository templates,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var found = await templates.GetByIdAsync(command.TenantId, command.TemplateId, ct);
        if (found.IsFailure)
            return Result.Failure<UpdateFormTemplateDraftOutcome>(found.Error);
        var concurrency = TemplateApplicationSupport.EnsureVersion(found.Value, command.RowVersion);
        if (concurrency.IsFailure)
            return Result.Failure<UpdateFormTemplateDraftOutcome>(concurrency.Error);
        var parsed = engine.ParseDefinition(command.DefinitionJson);
        if (parsed.IsFailure)
            return Result.Success(
                UpdateFormTemplateDraftOutcome.Invalid([TemplateApplicationSupport.ParseIssue(parsed.Error)])
            );
        var rejection = Reject(command, found.Value, parsed.Value, engine);
        if (rejection is not null)
            return rejection;
        var updated = ApplyDraft(command, found.Value, parsed.Value, engine);
        if (updated.IsFailure)
            return Result.Failure<UpdateFormTemplateDraftOutcome>(updated.Error);
        await unitOfWork.SaveChangesAsync(ct);
        return Saved(found.Value, parsed.Value, engine);
    }

    // Null si el borrador puede guardarse; si no, el error o los issues que lo impiden.
    private static Result<UpdateFormTemplateDraftOutcome>? Reject(
        UpdateFormTemplateDraftCommand command,
        TaxVision.Tasks.ClientForms.Domain.Templates.FormTemplate template,
        FormDefinition definition,
        IFormEngine engine
    )
    {
        var safeText = StaffTextGuard.Validate(definition.Title);
        if (safeText.IsFailure)
            return Result.Failure<UpdateFormTemplateDraftOutcome>(safeText.Error);
        var ruleSets = TemplateApplicationSupport.EnsureRuleSetsAllowed(
            engine,
            template.Draft.Json,
            definition,
            command.CanConfigureActions
        );
        if (ruleSets.IsFailure)
            return Result.Failure<UpdateFormTemplateDraftOutcome>(ruleSets.Error);
        var draftReport = engine.ValidateForDraft(definition);
        return draftReport.IsValid ? null : Result.Success(UpdateFormTemplateDraftOutcome.Invalid(draftReport.Issues));
    }

    private static Result ApplyDraft(
        UpdateFormTemplateDraftCommand command,
        TaxVision.Tasks.ClientForms.Domain.Templates.FormTemplate template,
        FormDefinition definition,
        IFormEngine engine
    )
    {
        var document = TemplateApplicationSupport.Document(engine, engine.Canonicalize(definition));
        if (document.IsFailure)
            return Result.Failure(document.Error);
        return template.UpdateDraft(
            document.Value.Document,
            document.Value.Hash,
            TemplateApplicationSupport.ToDomain(definition.Purpose),
            command.ByUserId,
            DateTime.UtcNow
        );
    }

    private static Result<UpdateFormTemplateDraftOutcome> Saved(
        TaxVision.Tasks.ClientForms.Domain.Templates.FormTemplate template,
        FormDefinition definition,
        IFormEngine engine
    ) =>
        Result.Success(
            UpdateFormTemplateDraftOutcome.Saved(
                Convert.ToBase64String(template.RowVersion),
                template.DraftHash.Value,
                engine.ValidateForPublish(definition).Issues
            )
        );
}
