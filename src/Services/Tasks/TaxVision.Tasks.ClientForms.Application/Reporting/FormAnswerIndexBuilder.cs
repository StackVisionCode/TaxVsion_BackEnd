using System.Text.Json;
using TaxVision.DataCollection.Engine.Answers;
using TaxVision.DataCollection.Engine.Definitions;
using TaxVision.Tasks.ClientForms.Domain.Requests;

namespace TaxVision.Tasks.ClientForms.Application.Reporting;

/// <summary>Metadatos de la revisión completada que acompañan a cada fila del índice.</summary>
public sealed record FormAnswerIndexContext(
    Guid TenantId,
    Guid FormRequestId,
    Guid FormTemplateId,
    int VersionNumber,
    Guid? CustomerId,
    Guid? ParticipantId,
    int RevisionNumber,
    DateTime CompletedAtUtc
);

/// <summary>
/// Función pura: recorre la definición y arma una fila de índice por cada valor de componente
/// <c>reportable</c> contestado. En repeaters, una fila por fila de la sección (RowIndex 1-based).
/// Las respuestas ya vienen saneadas de ocultos desde el envío.
/// </summary>
public static class FormAnswerIndexBuilder
{
    private static readonly JsonSerializerOptions RowOptions = new() { PropertyNameCaseInsensitive = true };

    public static IReadOnlyList<FormAnswerIndexEntry> Build(
        FormDefinition definition,
        FormAnswers answers,
        FormAnswerIndexContext context
    )
    {
        var entries = new List<FormAnswerIndexEntry>();
        foreach (var page in definition.Pages)
        foreach (var section in page.Sections)
        {
            if (section.Repeat is not null)
                AppendRepeater(entries, section, answers, context);
            else
                AppendFlat(entries, section, answers, context);
        }
        return entries;
    }

    private static void AppendFlat(
        List<FormAnswerIndexEntry> entries,
        FormSection section,
        FormAnswers answers,
        FormAnswerIndexContext context
    )
    {
        foreach (var component in section.Components)
        {
            if (!FormReportSchema.IsReportable(component))
                continue;
            var value = Get(answers.Values, component.Id);
            foreach (var cell in FormReportValueNormalizer.Normalize(component.Type, value))
                entries.Add(Entry(context, component.Key!, null, cell));
        }
    }

    private static void AppendRepeater(
        List<FormAnswerIndexEntry> entries,
        FormSection section,
        FormAnswers answers,
        FormAnswerIndexContext context
    )
    {
        var rows = ReadRows(answers, section.Id);
        for (var index = 0; index < rows.Count; index++)
        {
            foreach (var component in section.Components)
            {
                if (!FormReportSchema.IsReportable(component))
                    continue;
                var value = Get(rows[index].Values, component.Id);
                foreach (var cell in FormReportValueNormalizer.Normalize(component.Type, value))
                    entries.Add(Entry(context, component.Key!, index + 1, cell));
            }
        }
    }

    private static FormAnswerIndexEntry Entry(
        FormAnswerIndexContext context,
        string key,
        int? rowIndex,
        FormReportCell cell
    ) =>
        FormAnswerIndexEntry.Create(
            context.TenantId,
            context.FormRequestId,
            context.FormTemplateId,
            context.VersionNumber,
            context.CustomerId,
            context.ParticipantId,
            context.RevisionNumber,
            key,
            rowIndex,
            cell.Text,
            cell.Number,
            cell.Date,
            context.CompletedAtUtc
        );

    private static JsonElement Get(IReadOnlyDictionary<string, JsonElement> values, string key) =>
        values.TryGetValue(key, out var value) ? value : default;

    private static IReadOnlyList<RepeaterRow> ReadRows(FormAnswers answers, string sectionId)
    {
        var value = Get(answers.Values, sectionId);
        if (value.ValueKind != JsonValueKind.Object)
            return [];
        try
        {
            return value.Deserialize<RepeaterRows>(RowOptions)?.Rows ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
