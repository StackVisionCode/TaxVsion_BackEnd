using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.DataCollection.Engine;
using TaxVision.Tasks.ClientForms.Application.Common;
using TaxVision.Tasks.ClientForms.Application.Common.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Templates.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Templates.Responses;
using TaxVision.Tasks.ClientForms.Domain.Audit;

namespace TaxVision.Tasks.ClientForms.Application.Templates.Commands;

public sealed record PublishFormTemplateCommand(Guid TenantId, Guid ByUserId, Guid TemplateId, byte[] RowVersion);

public static class PublishFormTemplateHandler
{
    public static async Task<Result<PublishFormTemplateOutcome>> Handle(
        PublishFormTemplateCommand command,
        IFormEngine engine,
        IFormTemplateRepository templates,
        IFormTemplateVersionRepository versions,
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
            return Result.Success(PublishFormTemplateOutcome.Invalid([TemplateApplicationSupport.ParseIssue(parsed.Error)]));
        var safeText = StaffTextGuard.Validate(parsed.Value.Title);
        if (safeText.IsFailure)
            return Result.Failure<PublishFormTemplateOutcome>(safeText.Error);
        var report = engine.ValidateForPublish(parsed.Value);
        if (!report.IsValid)
            return Result.Success(PublishFormTemplateOutcome.Invalid(report.Issues));
        var published = found.Value.Publish(
            engine.Canonicalize(parsed.Value),
            parsed.Value.Title,
            TemplateApplicationSupport.ToDomain(parsed.Value.Presentation),
            TemplateApplicationSupport.CountComponents(parsed.Value),
            command.ByUserId,
            DateTime.UtcNow
        );
        if (published.IsFailure)
            return Result.Failure<PublishFormTemplateOutcome>(published.Error);
        versions.Add(published.Value);
        audit.Record(command.TenantId, found.Value.Id, null, FormAuditAction.TemplatePublished, command.ByUserId);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(PublishFormTemplateOutcome.Success(FormTemplateVersionResponse.From(published.Value)));
    }
}
