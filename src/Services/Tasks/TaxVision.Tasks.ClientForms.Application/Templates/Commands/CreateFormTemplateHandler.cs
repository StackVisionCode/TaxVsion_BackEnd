using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.DataCollection.Engine;
using TaxVision.Tasks.ClientForms.Application.Common.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Templates.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Templates.Responses;
using TaxVision.Tasks.ClientForms.Domain.Audit;
using TaxVision.Tasks.ClientForms.Domain.Templates;
using TaxVision.Tasks.ClientForms.Domain.ValueObjects;

namespace TaxVision.Tasks.ClientForms.Application.Templates.Commands;

public sealed record CreateFormTemplateCommand(
    Guid TenantId,
    Guid ByUserId,
    string? Name,
    string? Description,
    FormPurpose Purpose,
    FormPresentation Presentation
);

public static class CreateFormTemplateHandler
{
    public static async Task<Result<FormTemplateDetailResponse>> Handle(
        CreateFormTemplateCommand command,
        IFormEngine engine,
        IFormTemplateRepository templates,
        IFormAuditLog audit,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var metadata = TemplateApplicationSupport.Metadata(command.Name, command.Description);
        if (metadata.IsFailure)
            return Result.Failure<FormTemplateDetailResponse>(metadata.Error);
        var canonical = engine.Canonicalize(
            TemplateApplicationSupport.EmptyDefinition(metadata.Value.Name.Value, command.Purpose, command.Presentation)
        );
        var created = FormTemplate.Create(
            command.TenantId,
            metadata.Value.Name,
            metadata.Value.Description,
            new FormDefinitionDocument(canonical.Json),
            ContentHash.Create(canonical.Sha256).Value,
            command.Purpose,
            command.ByUserId,
            DateTime.UtcNow
        );
        if (created.IsFailure)
            return Result.Failure<FormTemplateDetailResponse>(created.Error);
        templates.Add(created.Value);
        audit.Record(command.TenantId, created.Value.Id, null, FormAuditAction.TemplateCreated, command.ByUserId);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(FormTemplateDetailResponse.From(created.Value));
    }
}
