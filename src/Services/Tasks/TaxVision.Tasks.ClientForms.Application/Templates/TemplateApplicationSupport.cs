using BuildingBlocks.Results;
using TaxVision.DataCollection.Engine;
using TaxVision.DataCollection.Engine.Definitions;
using TaxVision.DataCollection.Engine.Serialization;
using TaxVision.DataCollection.Engine.Validation;
using TaxVision.Tasks.ClientForms.Application.Common;
using TaxVision.Tasks.ClientForms.Domain.Templates;
using TaxVision.Tasks.ClientForms.Domain.ValueObjects;
using DomainPresentation = TaxVision.Tasks.ClientForms.Domain.Templates.FormPresentation;
using DomainPurpose = TaxVision.Tasks.ClientForms.Domain.Templates.FormPurpose;
using EnginePresentation = TaxVision.DataCollection.Engine.Definitions.FormPresentation;
using EnginePurpose = TaxVision.DataCollection.Engine.Definitions.FormPurpose;

namespace TaxVision.Tasks.ClientForms.Application.Templates;

internal static class TemplateApplicationSupport
{
    internal static Result EnsureVersion(FormTemplate template, byte[] expected) =>
        template.RowVersion.AsSpan().SequenceEqual(expected)
            ? Result.Success()
            : Result.Failure(FormTemplateErrors.ConcurrencyConflict);

    internal static Result<(FormTemplateName Name, FormDescription? Description)> Metadata(
        string? name,
        string? description
    )
    {
        var safeText = StaffTextGuard.Validate(name, description);
        if (safeText.IsFailure)
            return Result.Failure<(FormTemplateName, FormDescription?)>(safeText.Error);
        var parsedName = FormTemplateName.Create(name);
        if (parsedName.IsFailure)
            return Result.Failure<(FormTemplateName, FormDescription?)>(parsedName.Error);
        var parsedDescription = FormDescription.Create(description);
        return parsedDescription.IsFailure
            ? Result.Failure<(FormTemplateName, FormDescription?)>(parsedDescription.Error)
            : Result.Success((parsedName.Value, parsedDescription.Value));
    }

    internal static Result EnsureKnownOptions(DomainPurpose purpose, DomainPresentation presentation)
    {
        if (!Enum.IsDefined(purpose))
            return Result.Failure(FormTemplateErrors.InvalidPurpose);
        return Enum.IsDefined(presentation) ? Result.Success() : Result.Failure(FormTemplateErrors.InvalidPresentation);
    }

    // El documento canónico respeta el límite configurado, igual que el JSON de entrada.
    internal static Result<(FormDefinitionDocument Document, ContentHash Hash)> Document(
        IFormEngine engine,
        CanonicalDefinition canonical
    )
    {
        var document = FormDefinitionDocument.Create(canonical.Json, engine.Limits.MaxDefinitionBytes);
        var hash = ContentHash.Create(canonical.Sha256);
        if (document.IsFailure)
            return Result.Failure<(FormDefinitionDocument, ContentHash)>(document.Error);
        return hash.IsFailure
            ? Result.Failure<(FormDefinitionDocument, ContentHash)>(hash.Error)
            : Result.Success((document.Value, hash.Value));
    }

    internal static FormDefinition EmptyDefinition(
        string title,
        DomainPurpose purpose,
        DomainPresentation presentation
    ) =>
        new()
        {
            SchemaVersion = 1,
            Title = title,
            Purpose = ToEngine(purpose),
            Presentation = ToEngine(presentation),
            Pages =
            [
                new FormPage
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Title = "Page 1",
                    Sections = [new FormSection { Id = Guid.NewGuid().ToString("N"), Components = [] }],
                },
            ],
        };

    internal static ValidationIssue ParseIssue(Error error) => new(error.Code, "$", null, null, error.Message);

    internal static DomainPurpose ToDomain(EnginePurpose purpose) => (DomainPurpose)(int)purpose;

    internal static DomainPresentation ToDomain(EnginePresentation presentation) =>
        (DomainPresentation)(int)presentation;

    internal static EnginePurpose ToEngine(DomainPurpose purpose) => (EnginePurpose)(int)purpose;

    internal static EnginePresentation ToEngine(DomainPresentation presentation) =>
        (EnginePresentation)(int)presentation;

    internal static int CountComponents(FormDefinition definition) =>
        definition.Pages.Sum(page => page.Sections.Sum(section => section.Components.Count));
}
