using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.DataCollection.Engine;
using TaxVision.DataCollection.Engine.Definitions;
using TaxVision.Tasks.ClientForms.Application.Common.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Templates.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Templates.Responses;
using TaxVision.Tasks.ClientForms.Domain.Audit;
using TaxVision.Tasks.ClientForms.Domain.Templates;
using TaxVision.Tasks.ClientForms.Domain.ValueObjects;

namespace TaxVision.Tasks.ClientForms.Application.Templates.Commands;

public sealed record DuplicateFormTemplateCommand(
    Guid TenantId,
    Guid ByUserId,
    Guid TemplateId,
    int? FromVersionNumber,
    bool CanConfigureActions
);

public static class DuplicateFormTemplateHandler
{
    public static async Task<Result<FormTemplateDetailResponse>> Handle(
        DuplicateFormTemplateCommand command,
        IFormEngine engine,
        IFormTemplateRepository templates,
        IFormTemplateVersionRepository versions,
        IFormAuditLog audit,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var source = await templates.GetByIdAsync(command.TenantId, command.TemplateId, ct);
        if (source.IsFailure)
            return Result.Failure<FormTemplateDetailResponse>(source.Error);
        var json = await SourceJsonAsync(command, source.Value, versions, ct);
        if (json.IsFailure)
            return Result.Failure<FormTemplateDetailResponse>(json.Error);
        var parsed = engine.ParseDefinition(json.Value);
        if (parsed.IsFailure)
            return Result.Failure<FormTemplateDetailResponse>(FormTemplateErrors.InvalidDefinition);
        var created = CreateCopy(command, source.Value, engine, parsed.Value);
        if (created.IsFailure)
            return Result.Failure<FormTemplateDetailResponse>(created.Error);
        templates.Add(created.Value);
        audit.Record(command.TenantId, created.Value.Id, null, FormAuditAction.TemplateDuplicated, command.ByUserId);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(FormTemplateDetailResponse.From(created.Value));
    }

    // Copia el borrador o, si se pide, una versión publicada de la misma plantilla.
    private static async Task<Result<string>> SourceJsonAsync(
        DuplicateFormTemplateCommand command,
        FormTemplate source,
        IFormTemplateVersionRepository versions,
        CancellationToken ct
    )
    {
        if (!command.FromVersionNumber.HasValue)
            return Result.Success(source.Draft.Json);
        var version = await versions.GetAsync(
            command.TenantId,
            command.TemplateId,
            command.FromVersionNumber.Value,
            ct
        );
        return version.IsFailure
            ? Result.Failure<string>(version.Error)
            : Result.Success(version.Value.Definition.Json);
    }

    private static Result<FormTemplate> CreateCopy(
        DuplicateFormTemplateCommand command,
        FormTemplate source,
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
        return FormTemplate.Create(
            command.TenantId,
            FormTemplateName.Create(CopyName(source.Name.Value)).Value,
            source.Description,
            document.Value.Document,
            document.Value.Hash,
            TemplateApplicationSupport.ToDomain(definition.Purpose),
            command.ByUserId,
            DateTime.UtcNow
        );
    }

    private static string CopyName(string name) =>
        name.Length + 7 <= FormTemplateName.MaxLength
            ? name + " (copy)"
            : name[..(FormTemplateName.MaxLength - 7)] + " (copy)";
}
