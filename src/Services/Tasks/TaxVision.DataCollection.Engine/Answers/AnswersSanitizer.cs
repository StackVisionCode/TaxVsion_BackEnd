using System.Text.Json;
using TaxVision.DataCollection.Engine.Definitions;
using TaxVision.DataCollection.Engine.Visibility;

namespace TaxVision.DataCollection.Engine.Answers;

internal static class AnswersSanitizer
{
    internal static FormAnswers RemoveHidden(
        FormDefinition definition,
        FormAnswers answers,
        VisibilityMap visibility
    )
    {
        var values = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var page in definition.Pages)
        {
            if (!visibility.IsVisible(page.Id))
                continue;
            foreach (var section in page.Sections)
            {
                if (!visibility.IsVisible(section.Id))
                    continue;
                if (section.Repeat is null)
                    CopyRegularValues(section, answers, visibility, values);
                else
                    CopyRepeaterValues(section, answers, visibility, values);
            }
        }
        return answers.WithValues(values);
    }

    private static void CopyRegularValues(
        FormSection section,
        FormAnswers answers,
        VisibilityMap visibility,
        IDictionary<string, JsonElement> target
    )
    {
        foreach (var component in section.Components)
        {
            if (visibility.IsVisible(component.Id) && answers.Values.TryGetValue(component.Id, out var value))
                target[component.Id] = value.Clone();
        }
    }

    private static void CopyRepeaterValues(
        FormSection section,
        FormAnswers answers,
        VisibilityMap visibility,
        IDictionary<string, JsonElement> target
    )
    {
        var rows = new List<object>();
        foreach (var row in AnswersReader.ReadRows(answers, section.Id))
        {
            var rowValues = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var component in section.Components)
            {
                if (visibility.IsVisible(component.Id, row.RowId) && row.Values.TryGetValue(component.Id, out var value))
                    rowValues[component.Id] = value.Clone();
            }
            rows.Add(new { rowId = row.RowId, values = rowValues });
        }
        target[section.Id] = JsonSerializer.SerializeToElement(new { rows });
    }
}
