using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.DataCollection.Engine;
using TaxVision.DataCollection.Engine.Definitions;
using TaxVision.Tasks.ClientForms.Application.Common;
using TaxVision.Tasks.ClientForms.Application.Templates.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Templates.Responses;
using TaxVision.Tasks.ClientForms.Domain.ValueObjects;

namespace TaxVision.Tasks.ClientForms.Application.Templates.Commands;

public sealed record UpdateFormTemplateDraftCommand(
    Guid TenantId,
    Guid ByUserId,
    Guid TemplateId,
    string DefinitionJson,
    byte[] RowVersion
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
            return Result.Success(UpdateFormTemplateDraftOutcome.Invalid([TemplateApplicationSupport.ParseIssue(parsed.Error)]));
        var safeText = StaffTextGuard.Validate(parsed.Value.Title);
        if (safeText.IsFailure)
            return Result.Failure<UpdateFormTemplateDraftOutcome>(safeText.Error);
        var draftReport = engine.ValidateForDraft(parsed.Value);
        if (!draftReport.IsValid)
            return Result.Success(UpdateFormTemplateDraftOutcome.Invalid(draftReport.Issues));
        var canonical = engine.Canonicalize(parsed.Value);
        var updated = found.Value.UpdateDraft(
            new FormDefinitionDocument(canonical.Json),
            ContentHash.Create(canonical.Sha256).Value,
            TemplateApplicationSupport.ToDomain(parsed.Value.Purpose),
            command.ByUserId,
            DateTime.UtcNow
        );
        if (updated.IsFailure)
            return Result.Failure<UpdateFormTemplateDraftOutcome>(updated.Error);
        await unitOfWork.SaveChangesAsync(ct);
        return Saved(found.Value, parsed.Value, engine);
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
