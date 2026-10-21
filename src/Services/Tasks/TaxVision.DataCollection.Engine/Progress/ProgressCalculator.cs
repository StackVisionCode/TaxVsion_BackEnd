using System.Text.Json;
using TaxVision.DataCollection.Engine.Answers;
using TaxVision.DataCollection.Engine.Components;
using TaxVision.DataCollection.Engine.Conditions;
using TaxVision.DataCollection.Engine.Definitions;
using TaxVision.DataCollection.Engine.Validation;
using TaxVision.DataCollection.Engine.Visibility;

namespace TaxVision.DataCollection.Engine.Progress;

internal sealed class ProgressCalculator(ComponentRegistry registry)
{
    internal FormProgress Calculate(FormDefinition definition, FormAnswers answers, VisibilityMap visibility)
    {
        var index = DefinitionIndex.Create(definition, registry);
        var required = 0;
        var answered = 0;
        foreach (var page in definition.Pages)
        {
            if (!visibility.IsVisible(page.Id))
                continue;
            foreach (var section in page.Sections)
            {
                if (!visibility.IsVisible(section.Id))
                    continue;
                if (section.Repeat is null)
                    CountRegular(section, answers, index, visibility, ref required, ref answered);
                else
                    CountRows(section, answers, index, visibility, ref required, ref answered);
            }
        }
        return new FormProgress(required, answered);
    }

    private static void CountRegular(
        FormSection section,
        FormAnswers answers,
        DefinitionIndex index,
        VisibilityMap visibility,
        ref int required,
        ref int answered
    )
    {
        foreach (var component in section.Components)
        {
            if (!visibility.IsVisible(component.Id) || index.Components[component.Id].ComponentType?.Kind != ComponentKind.Input)
                continue;
            var isRequired = component.Required
                || component.RequiredWhen is not null
                    && ConditionEvaluator.Evaluate(component.RequiredWhen, answers, index, visibility);
            if (!isRequired)
                continue;
            required++;
            if (CountsAsAnswered(component, AnswersReader.Get(answers.Values, component.Id)))
                answered++;
        }
    }

    private static void CountRows(
        FormSection section,
        FormAnswers answers,
        DefinitionIndex index,
        VisibilityMap visibility,
        ref int required,
        ref int answered
    )
    {
        foreach (var row in AnswersReader.ReadRows(answers, section.Id))
        {
            foreach (var component in section.Components)
            {
                if (!visibility.IsVisible(component.Id, row.RowId) || index.Components[component.Id].ComponentType?.Kind != ComponentKind.Input)
                    continue;
                var isRequired = component.Required
                    || component.RequiredWhen is not null
                        && ConditionEvaluator.Evaluate(component.RequiredWhen, answers, index, visibility, row.RowId, row.Values);
                if (!isRequired)
                    continue;
                required++;
                if (CountsAsAnswered(component, AnswersReader.Get(row.Values, component.Id)))
                    answered++;
            }
        }
    }

    private static bool CountsAsAnswered(FormComponent component, JsonElement value) =>
        component.Type == "checkbox" && component.Required
            ? value.ValueKind == JsonValueKind.True
            : AnswersReader.IsAnswered(value, component.Type);
}
