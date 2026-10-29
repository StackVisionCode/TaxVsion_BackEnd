using TaxVision.DataCollection.Engine.Answers;
using TaxVision.DataCollection.Engine.Components;
using TaxVision.DataCollection.Engine.Conditions;
using TaxVision.DataCollection.Engine.Definitions;
using TaxVision.DataCollection.Engine.Validation;

namespace TaxVision.DataCollection.Engine.Visibility;

internal sealed class VisibilityEvaluator(DataCollectionLimits limits, ComponentRegistry registry)
{
    internal VisibilityMap Evaluate(FormDefinition definition, FormAnswers answers)
    {
        var index = DefinitionIndex.Create(definition, registry);
        var visibility = VisibilityMap.CreateMutable();

        foreach (var page in definition.Pages)
        {
            if (!ConditionEvaluator.Evaluate(page.VisibleWhen, answers, index, visibility))
                continue;
            visibility.MarkVisible(page.Id);

            foreach (var section in page.Sections)
            {
                if (!ConditionEvaluator.Evaluate(section.VisibleWhen, answers, index, visibility))
                    continue;
                visibility.MarkVisible(section.Id);

                if (section.Repeat is null)
                {
                    EvaluateComponents(section, answers, index, visibility, null);
                    continue;
                }
                var rows = AnswersReader.ReadRows(answers, section.Id);
                visibility.SetRowCount(section.Id, rows.Count);
                // Las filas por encima del máximo no se evalúan: la validación ya las rechaza.
                foreach (var row in rows.Take(MaxRows(section)))
                    EvaluateComponents(section, answers, index, visibility, row);
            }
        }
        return visibility;
    }

    private int MaxRows(FormSection section) => Math.Min(section.Repeat!.MaxRows, limits.MaxRepeaterRows);

    private static void EvaluateComponents(
        FormSection section,
        FormAnswers answers,
        DefinitionIndex index,
        VisibilityMap visibility,
        RepeaterRow? row
    )
    {
        foreach (var component in section.Components)
        {
            var componentVisible = ConditionEvaluator.Evaluate(
                component.VisibleWhen,
                answers,
                index,
                visibility,
                row?.RowId,
                row?.Values
            );
            if (row is not null)
                visibility.SetRowVisibility(component.Id, row.RowId, componentVisible);
            if (componentVisible)
                visibility.MarkVisible(component.Id);
        }
    }
}
