using BuildingBlocks.Results;
using TaxVision.DataCollection.Engine;
using TaxVision.DataCollection.Engine.Definitions;
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

    internal static FormDefinition EmptyDefinition(string title, DomainPurpose purpose, DomainPresentation presentation) =>
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
    internal static DomainPresentation ToDomain(EnginePresentation presentation) => (DomainPresentation)(int)presentation;
    internal static EnginePurpose ToEngine(DomainPurpose purpose) => (EnginePurpose)(int)purpose;
    internal static EnginePresentation ToEngine(DomainPresentation presentation) => (EnginePresentation)(int)presentation;

    internal static int CountComponents(FormDefinition definition) =>
        definition.Pages.Sum(page => page.Sections.Sum(section => section.Components.Count));
}
