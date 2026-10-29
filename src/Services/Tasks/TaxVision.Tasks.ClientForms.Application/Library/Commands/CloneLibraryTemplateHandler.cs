using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.DataCollection.Engine;
using TaxVision.DataCollection.Engine.Definitions;
using TaxVision.Tasks.ClientForms.Application.Common.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Templates;
using TaxVision.Tasks.ClientForms.Application.Templates.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Templates.Responses;
using TaxVision.Tasks.ClientForms.Domain.Audit;
using TaxVision.Tasks.ClientForms.Domain.Templates;
using TaxVision.Tasks.ClientForms.Domain.ValueObjects;

namespace TaxVision.Tasks.ClientForms.Application.Library.Commands;

public sealed record CloneLibraryTemplateCommand(
    Guid TenantId,
    Guid ByUserId,
    string LibraryKey,
    bool CanConfigureActions
);

public static class CloneLibraryTemplateHandler
{
    public static async Task<Result<FormTemplateDetailResponse>> Handle(
        CloneLibraryTemplateCommand command,
        IFormLibraryRepository library,
        IFormEngine engine,
        IFormTemplateRepository templates,
        IFormAuditLog audit,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var detail = await library.GetByKeyAsync(command.LibraryKey, ct);
        if (detail is null)
            return Result.Failure<FormTemplateDetailResponse>(FormLibraryErrors.NotFound);
        var parsed = engine.ParseDefinition(detail.DefinitionJson);
        if (parsed.IsFailure)
            return Result.Failure<FormTemplateDetailResponse>(FormTemplateErrors.InvalidDefinition);
        var created = CreateClone(command, detail, engine, parsed.Value);
        if (created.IsFailure)
            return Result.Failure<FormTemplateDetailResponse>(created.Error);
        templates.Add(created.Value);
        audit.Record(
            command.TenantId,
            created.Value.Id,
            null,
            FormAuditAction.TemplateClonedFromLibrary,
            command.ByUserId
        );
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(FormTemplateDetailResponse.From(created.Value));
    }

    private static Result<FormTemplate> CreateClone(
        CloneLibraryTemplateCommand command,
        LibraryTemplateDetailRecord detail,
        IFormEngine engine,
        FormDefinition definition
    )
    {
        var allowed = TemplateApplicationSupport.EnsureRuleSetsAllowed(
            engine,
            null,
            definition,
            command.CanConfigureActions
        );
        if (allowed.IsFailure)
            return Result.Failure<FormTemplate>(allowed.Error);
        var document = TemplateApplicationSupport.Document(engine, engine.Canonicalize(definition));
        if (document.IsFailure)
            return Result.Failure<FormTemplate>(document.Error);
        var origin = LibraryOrigin.Create(detail.LibraryKey, detail.SourceTemplateId, detail.VersionNumber);
        if (origin.IsFailure)
            return Result.Failure<FormTemplate>(origin.Error);
        return FormTemplate.Create(
            command.TenantId,
            FormTemplateName.Create(detail.Name).Value,
            detail.Description is null ? null : FormDescription.Create(detail.Description).Value,
            document.Value.Document,
            document.Value.Hash,
            TemplateApplicationSupport.ToDomain(definition.Purpose),
            command.ByUserId,
            DateTime.UtcNow,
            origin.Value
        );
    }
}
