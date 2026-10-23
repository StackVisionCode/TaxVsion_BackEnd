using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.DataCollection.Engine;
using TaxVision.Tasks.ClientForms.Application.Common.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Templates.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Templates.Responses;
using TaxVision.Tasks.ClientForms.Domain.Audit;
using TaxVision.Tasks.ClientForms.Domain.Templates;

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
        var created = Build(command, engine);
        if (created.IsFailure)
            return Result.Failure<FormTemplateDetailResponse>(created.Error);
        templates.Add(created.Value);
        audit.Record(command.TenantId, created.Value.Id, null, FormAuditAction.TemplateCreated, command.ByUserId);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(FormTemplateDetailResponse.From(created.Value));
    }

    private static Result<FormTemplate> Build(CreateFormTemplateCommand command, IFormEngine engine)
    {
        var options = TemplateApplicationSupport.EnsureKnownOptions(command.Purpose, command.Presentation);
        if (options.IsFailure)
            return Result.Failure<FormTemplate>(options.Error);
        var metadata = TemplateApplicationSupport.Metadata(command.Name, command.Description);
        if (metadata.IsFailure)
            return Result.Failure<FormTemplate>(metadata.Error);
        var empty = TemplateApplicationSupport.EmptyDefinition(
            metadata.Value.Name.Value,
            command.Purpose,
            command.Presentation
        );
        var document = TemplateApplicationSupport.Document(engine, engine.Canonicalize(empty));
        if (document.IsFailure)
            return Result.Failure<FormTemplate>(document.Error);
        return FormTemplate.Create(
            command.TenantId,
            metadata.Value.Name,
            metadata.Value.Description,
            document.Value.Document,
            document.Value.Hash,
            command.Purpose,
            command.ByUserId,
            DateTime.UtcNow
        );
    }
}
