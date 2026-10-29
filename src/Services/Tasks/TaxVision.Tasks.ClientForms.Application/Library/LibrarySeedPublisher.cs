using BuildingBlocks.Results;
using TaxVision.DataCollection.Engine;
using TaxVision.Tasks.ClientForms.Application.Templates;
using TaxVision.Tasks.ClientForms.Domain.Templates;
using TaxVision.Tasks.ClientForms.Domain.ValueObjects;
using TaxVision.Tasks.ClientForms.Domain.Versions;

namespace TaxVision.Tasks.ClientForms.Application.Library;

/// <summary>
/// Publica el borrador de una plantilla sembrada y la lista en la biblioteca, para que los tenants la
/// vean sin revisión previa (decisión del dueño, 2026-10-28). Valida con el engine igual que un publish.
/// </summary>
public static class LibrarySeedPublisher
{
    public static Result<FormTemplateVersion> PublishAndList(
        IFormEngine engine,
        FormTemplate template,
        int targetCatalogVersion,
        Guid platformTenantId,
        Guid byUserId,
        DateTime nowUtc
    )
    {
        var parsed = engine.ParseDefinition(template.Draft.Json);
        if (parsed.IsFailure)
            return Result.Failure<FormTemplateVersion>(parsed.Error);
        if (!engine.ValidateForPublish(parsed.Value).IsValid)
            return Result.Failure<FormTemplateVersion>(FormTemplateErrors.InvalidDefinition);

        var rules = engine.CanonicalizeRuleSets(parsed.Value);
        var ruleSets = FormVersionRuleSets.Create(rules.MappingSetJson, rules.ActionSetJson, targetCatalogVersion);
        if (ruleSets.IsFailure)
            return Result.Failure<FormTemplateVersion>(ruleSets.Error);
        var version = template.Publish(
            engine.Canonicalize(parsed.Value),
            parsed.Value.Title,
            TemplateApplicationSupport.ToDomain(parsed.Value.Presentation),
            TemplateApplicationSupport.CountComponents(parsed.Value),
            byUserId,
            nowUtc,
            ruleSets.Value
        );
        if (version.IsFailure)
            return version;
        var listed = template.ListInLibrary(template.LibraryKey!, platformTenantId, nowUtc);
        return listed.IsFailure ? Result.Failure<FormTemplateVersion>(listed.Error) : version;
    }
}
