using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.DataCollection.Engine;
using TaxVision.DataCollection.Engine.Definitions;
using TaxVision.Tasks.ClientForms.Application.Common;
using TaxVision.Tasks.ClientForms.Application.Common.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Consents;
using TaxVision.Tasks.ClientForms.Application.Consents.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Templates.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Templates.Responses;
using TaxVision.Tasks.ClientForms.Domain.Audit;
using TaxVision.Tasks.ClientForms.Domain.Templates;
using TaxVision.Tasks.ClientForms.Domain.Versions;

namespace TaxVision.Tasks.ClientForms.Application.Templates.Commands;

public sealed record PublishFormTemplateCommand(Guid TenantId, Guid ByUserId, Guid TemplateId, byte[] RowVersion);

public static class PublishFormTemplateHandler
{
    public static async Task<Result<PublishFormTemplateOutcome>> Handle(
        PublishFormTemplateCommand command,
        IFormEngine engine,
        IFormTemplateRepository templates,
        IFormTemplateVersionRepository versions,
        IConsentNoticeRepository consentNotices,
        IFormAuditLog audit,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var found = await templates.GetByIdAsync(command.TenantId, command.TemplateId, ct);
        if (found.IsFailure)
            return Result.Failure<PublishFormTemplateOutcome>(found.Error);
        var concurrency = TemplateApplicationSupport.EnsureVersion(found.Value, command.RowVersion);
        if (concurrency.IsFailure)
            return Result.Failure<PublishFormTemplateOutcome>(concurrency.Error);
        var parsed = engine.ParseDefinition(found.Value.Draft.Json);
        if (parsed.IsFailure)
            return Result.Success(
                PublishFormTemplateOutcome.Invalid([TemplateApplicationSupport.ParseIssue(parsed.Error)])
            );
        var rejection = await ValidateDefinitionAsync(command, engine, parsed.Value, consentNotices, ct);
        if (rejection is not null)
            return rejection;

        var published = Publish(command, engine, found.Value, parsed.Value);
        if (published.IsFailure)
            return Result.Failure<PublishFormTemplateOutcome>(published.Error);
        versions.Add(published.Value);
        audit.Record(command.TenantId, found.Value.Id, null, FormAuditAction.TemplatePublished, command.ByUserId);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(PublishFormTemplateOutcome.Success(FormTemplateVersionResponse.From(published.Value)));
    }

    // Null cuando la definición puede publicarse; si no, el error o los issues que lo impiden.
    private static async Task<Result<PublishFormTemplateOutcome>?> ValidateDefinitionAsync(
        PublishFormTemplateCommand command,
        IFormEngine engine,
        FormDefinition definition,
        IConsentNoticeRepository consentNotices,
        CancellationToken ct
    )
    {
        var safeText = StaffTextGuard.Validate(definition.Title);
        if (safeText.IsFailure)
            return Result.Failure<PublishFormTemplateOutcome>(safeText.Error);
        var report = engine.ValidateForPublish(definition);
        if (!report.IsValid)
            return Result.Success(PublishFormTemplateOutcome.Invalid(report.Issues));
        var consentIssues = await FormConsentDefinitionSupport.ValidateForPublishAsync(
            command.TenantId,
            definition,
            consentNotices,
            ct
        );
        return consentIssues.Count > 0 ? Result.Success(PublishFormTemplateOutcome.Invalid(consentIssues)) : null;
    }

    private static Result<FormTemplateVersion> Publish(
        PublishFormTemplateCommand command,
        IFormEngine engine,
        FormTemplate template,
        FormDefinition definition
    ) =>
        template.Publish(
            engine.Canonicalize(definition),
            definition.Title,
            TemplateApplicationSupport.ToDomain(definition.Presentation),
            TemplateApplicationSupport.CountComponents(definition),
            command.ByUserId,
            DateTime.UtcNow
        );
}
