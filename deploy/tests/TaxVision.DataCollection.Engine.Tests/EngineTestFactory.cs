using System.Text.Json;
using TaxVision.DataCollection.Engine;
using TaxVision.DataCollection.Engine.Answers;
using TaxVision.DataCollection.Engine.Conditions;
using TaxVision.DataCollection.Engine.Definitions;

namespace TaxVision.DataCollection.Engine.Tests;

internal static class EngineTestFactory
{
    internal static FormEngine Create(DataCollectionLimits? limits = null) => new(limits ?? new DataCollectionLimits());

    internal static FormDefinition Definition(
        string type = "text",
        object? props = null,
        bool required = false,
        DataClass? dataClass = null,
        string? label = "Your answer",
        Condition? requiredWhen = null
    ) =>
        new()
        {
            SchemaVersion = 1,
            Title = "Client intake",
            Purpose = FormPurpose.ClientIntake,
            Presentation = FormPresentation.Wizard,
            Pages =
            [
                new FormPage
                {
                    Id = "pg_abcdefgh",
                    Title = "About you",
                    Sections =
                    [
                        new FormSection
                        {
                            Id = "sc_abcdefgh",
                            Title = "Information",
                            Components =
                            [
                                new FormComponent
                                {
                                    Id = "fd_abcdefgh",
                                    Type = type,
                                    Label = IsInput(type) ? label : null,
                                    Required = required,
                                    RequiredWhen = requiredWhen,
                                    DataClass = dataClass,
                                    Props = JsonSerializer.SerializeToElement(props ?? new { }),
                                },
                            ],
                        },
                    ],
                },
            ],
        };

    internal static FormDefinition WithPages(FormDefinition source, IReadOnlyList<FormPage> pages) =>
        new()
        {
            SchemaVersion = source.SchemaVersion,
            Title = source.Title,
            Description = source.Description,
            Purpose = source.Purpose,
            Presentation = source.Presentation,
            Settings = source.Settings,
            Pages = pages,
            Mappings = source.Mappings,
            Actions = source.Actions,
        };

    internal static FormDefinition WithComponents(params FormComponent[] components) =>
        new()
        {
            SchemaVersion = 1,
            Title = "Client intake",
            Purpose = FormPurpose.ClientIntake,
            Presentation = FormPresentation.Wizard,
            Pages =
            [
                new FormPage
                {
                    Id = "pg_abcdefgh",
                    Title = "About you",
                    Sections =
                    [
                        new FormSection
                        {
                            Id = "sc_abcdefgh",
                            Title = "Information",
                            Components = components,
                        },
                    ],
                },
            ],
        };

    internal static FormAnswers Answers(object? value = null) =>
        new()
        {
            SchemaVersion = 1,
            Values = value is null
                ? new Dictionary<string, JsonElement>()
                : new Dictionary<string, JsonElement>
                {
                    ["fd_abcdefgh"] = JsonSerializer.SerializeToElement(value),
                },
        };

    private static bool IsInput(string type) =>
        type is not ("heading" or "paragraph" or "callout" or "divider" or "spacer" or "consent" or "signatureStep");
}
