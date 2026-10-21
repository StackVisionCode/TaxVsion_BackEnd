using TaxVision.DataCollection.Engine.Answers;
using TaxVision.DataCollection.Engine.Components;
using TaxVision.DataCollection.Engine.Conditions;
using TaxVision.DataCollection.Engine.Definitions;
using TaxVision.DataCollection.Engine.Validation;

namespace TaxVision.DataCollection.Engine.Visibility;

internal sealed class VisibilityEvaluator(ComponentRegistry registry)
{
    internal VisibilityMap Evaluate(FormDefinition definition, FormAnswers answers)
    {
        var index = DefinitionIndex.Create(definition, registry);
        var visible = new HashSet<string>(StringComparer.Ordinal);
        var rowVisibility = new Dictionary<(string ComponentId, string RowId), bool>();

        foreach (var page in definition.Pages)
        {
            var current = new VisibilityMap(visible, rowVisibility);
            var pageVisible = ConditionEvaluator.Evaluate(page.VisibleWhen, answers, index, current);
            if (!pageVisible)
                continue;
            visible.Add(page.Id);

            foreach (var section in page.Sections)
            {
                current = new VisibilityMap(visible, rowVisibility);
                var sectionVisible = ConditionEvaluator.Evaluate(section.VisibleWhen, answers, index, current);
                if (!sectionVisible)
                    continue;
                visible.Add(section.Id);

                if (section.Repeat is null)
                    EvaluateComponents(section, answers, index, visible, rowVisibility, null);
                else
                    foreach (var row in AnswersReader.ReadRows(answers, section.Id))
                        EvaluateComponents(section, answers, index, visible, rowVisibility, row);
            }
        }
        return new VisibilityMap(visible, rowVisibility);
    }

    private static void EvaluateComponents(
        FormSection section,
        FormAnswers answers,
        DefinitionIndex index,
        ISet<string> visible,
        IDictionary<(string ComponentId, string RowId), bool> rowVisibility,
        RepeaterRow? row
    )
    {
        foreach (var component in section.Components)
        {
            var current = new VisibilityMap(visible, rowVisibility);
            var componentVisible = ConditionEvaluator.Evaluate(
                component.VisibleWhen,
                answers,
                index,
                current,
                row?.RowId,
                row?.Values
            );
            if (row is null)
            {
                if (componentVisible)
                    visible.Add(component.Id);
            }
            else
            {
                rowVisibility[(component.Id, row.RowId)] = componentVisible;
                if (componentVisible)
                    visible.Add(component.Id);
            }
        }
    }
}
