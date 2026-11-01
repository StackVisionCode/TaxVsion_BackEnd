using System.Globalization;
using System.Text.Json;
using BuildingBlocks.Messaging.ClientFormsIntegrationEvents;
using TaxVision.DataCollection.Engine.Answers;
using TaxVision.DataCollection.Engine.Components;
using TaxVision.DataCollection.Engine.Definitions;
using TaxVision.DataCollection.Engine.Visibility;

namespace TaxVision.Tasks.ClientForms.Application.Actions;

/// <summary>
/// Arma el snapshot del Submission Summary a partir de la definición, las respuestas depuradas y el mapa de
/// visibilidad. Función pura: minimiza (sólo label→valor de lo visible e imprimible), enmascara el taxId,
/// excluye ocultos, ids internos, comentarios y auditoría, y rotula las filas de los repeaters.
/// </summary>
public static class SubmissionSummarySnapshotBuilder
{
    // Metadatos del taxId para enmascarar sin descifrar: componente/fila → últimos 4.
    public sealed record SensitiveMask(string ComponentId, string? RowId, string Last4);

    // Adjunto disponible: componente/fila → nombre de archivo (sin FileId).
    public sealed record AttachmentRef(string ComponentId, string? RowId, string FileName);

    public sealed record Header(
        string Title,
        string? CustomerDisplayName,
        int VersionNumber,
        int RevisionNumber,
        DateTime? SubmittedAtUtc,
        DateTime? CompletedAtUtc
    );

    private const int MaxValueChars = 2000;
    private static readonly ComponentRegistry Registry = new();
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static JsonElement GetValue(IReadOnlyDictionary<string, JsonElement> values, string key) =>
        values.TryGetValue(key, out var value) ? value : default;

    private static IReadOnlyList<RepeaterRow> ReadRows(FormAnswers answers, string sectionId)
    {
        var value = GetValue(answers.Values, sectionId);
        if (value.ValueKind != JsonValueKind.Object)
            return [];
        try
        {
            return value.Deserialize<RepeaterRows>(JsonOptions)?.Rows ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public static SubmissionSummarySnapshot Build(
        FormDefinition definition,
        FormAnswers answers,
        VisibilityMap visibility,
        IReadOnlyCollection<SensitiveMask> sensitiveMasks,
        IReadOnlyCollection<AttachmentRef> attachments,
        Header header
    )
    {
        var masks = BuildMaskLookup(sensitiveMasks);
        var pages = new List<SubmissionSummaryPage>();
        foreach (var page in definition.Pages)
        {
            if (!visibility.IsVisible(page.Id))
                continue;
            var sections = new List<SubmissionSummarySection>();
            foreach (var section in page.Sections)
            {
                if (!visibility.IsVisible(section.Id))
                    continue;
                var built = section.Repeat is null
                    ? BuildFlatSection(section, answers, visibility, masks)
                    : BuildRepeaterSection(section, answers, visibility, masks);
                if (built is not null)
                    sections.Add(built);
            }
            if (sections.Count > 0)
                pages.Add(new SubmissionSummaryPage(page.Title, sections));
        }

        return new SubmissionSummarySnapshot
        {
            Title = header.Title,
            CustomerDisplayName = header.CustomerDisplayName,
            VersionNumber = header.VersionNumber,
            RevisionNumber = header.RevisionNumber,
            SubmittedAtUtc = header.SubmittedAtUtc,
            CompletedAtUtc = header.CompletedAtUtc,
            Pages = pages,
            Attachments = BuildAttachments(definition, visibility, attachments),
        };
    }

    private static SubmissionSummarySection? BuildFlatSection(
        FormSection section,
        FormAnswers answers,
        VisibilityMap visibility,
        IReadOnlyDictionary<(string, string?), string> masks
    )
    {
        var items = new List<SubmissionSummaryItem>();
        foreach (var component in section.Components)
        {
            if (!visibility.IsVisible(component.Id))
                continue;
            var value = GetValue(answers.Values, component.Id);
            var item = BuildItem(component, value, masks, rowId: null);
            if (item is not null)
                items.Add(item);
        }
        return items.Count == 0 ? null : new SubmissionSummarySection(section.Title, items, []);
    }

    private static SubmissionSummarySection? BuildRepeaterSection(
        FormSection section,
        FormAnswers answers,
        VisibilityMap visibility,
        IReadOnlyDictionary<(string, string?), string> masks
    )
    {
        var rows = new List<SubmissionSummaryRow>();
        var allRows = ReadRows(answers, section.Id);
        var index = 0;
        foreach (var row in allRows)
        {
            index++;
            if (!visibility.IsVisible(section.Id, row.RowId))
                continue;
            var items = new List<SubmissionSummaryItem>();
            foreach (var component in section.Components)
            {
                var value = GetValue(row.Values, component.Id);
                var item = BuildItem(component, value, masks, row.RowId);
                if (item is not null)
                    items.Add(item);
            }
            if (items.Count > 0)
                rows.Add(new SubmissionSummaryRow(RowLabel(section.Repeat, index), items));
        }
        return rows.Count == 0 ? null : new SubmissionSummarySection(section.Title, [], rows);
    }

    private static SubmissionSummaryItem? BuildItem(
        FormComponent component,
        JsonElement value,
        IReadOnlyDictionary<(string, string?), string> masks,
        string? rowId
    )
    {
        if (!Registry.TryGet(component.Type, out var type) || type.Kind != ComponentKind.Input)
            return null; // Sólo entradas: sin encabezados, párrafos, divisores ni pasos de firma.
        if (component.Type == "file")
            return null; // Los adjuntos van en su propia lista, por nombre.

        var label = Label(component);
        if (component.Type == "taxId")
        {
            // Enmascarado con los últimos 4; nunca el valor en claro (vive aparte, cifrado).
            if (masks.TryGetValue((component.Id, rowId), out var last4))
                return new SubmissionSummaryItem(label, $"•••-••-{last4}");
            return null;
        }

        var rendered = RenderValue(component, value);
        return string.IsNullOrEmpty(rendered) ? null : new SubmissionSummaryItem(label, Truncate(rendered));
    }

    private static string? RenderValue(FormComponent component, JsonElement value)
    {
        if (value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            return null;
        return component.Type switch
        {
            "yesNo" => RenderYesNo(component, value),
            "checkbox" => value.ValueKind == JsonValueKind.True ? "Yes"
            : value.ValueKind == JsonValueKind.False ? "No"
            : null,
            "select" or "radio" => value.ValueKind == JsonValueKind.String
                ? OptionLabel(component, value.GetString()!)
                : null,
            "checkboxGroup" => RenderChoices(component, value),
            "address" => RenderAddress(value),
            "currency" => value.ValueKind == JsonValueKind.String ? value.GetString() : null,
            _ => value.ValueKind == JsonValueKind.String ? Clean(value.GetString()) : null,
        };
    }

    private static string? RenderYesNo(FormComponent component, JsonElement value)
    {
        if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            return null;
        var yes = PropString(component, "yesLabel") ?? "Yes";
        var no = PropString(component, "noLabel") ?? "No";
        return value.ValueKind == JsonValueKind.True ? yes : no;
    }

    private static string? RenderChoices(FormComponent component, JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Array)
            return null;
        var labels = new List<string>();
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
                labels.Add(OptionLabel(component, item.GetString()!));
        }
        return labels.Count == 0 ? null : string.Join(", ", labels);
    }

    private static string RenderAddress(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object)
            return string.Empty;
        var parts = new List<string>();
        void Add(string key)
        {
            if (value.TryGetProperty(key, out var p) && p.ValueKind == JsonValueKind.String)
            {
                var s = p.GetString();
                if (!string.IsNullOrWhiteSpace(s))
                    parts.Add(s.Trim());
            }
        }
        Add("line1");
        Add("line2");
        Add("city");
        Add("state");
        Add("postalCode");
        Add("country");
        return string.Join(", ", parts);
    }

    // Etiqueta de la opción elegida (props.options); si ya no existe, el valor.
    private static string OptionLabel(FormComponent component, string optionValue) =>
        ComponentOptions.LabelOf(component, optionValue);

    private static IReadOnlyList<SubmissionSummaryAttachment> BuildAttachments(
        FormDefinition definition,
        VisibilityMap visibility,
        IReadOnlyCollection<AttachmentRef> attachments
    )
    {
        if (attachments.Count == 0)
            return [];
        var labels = definition
            .Pages.SelectMany(p => p.Sections)
            .SelectMany(s => s.Components)
            .Where(c => c.Type == "file")
            .ToDictionary(c => c.Id, Label, StringComparer.Ordinal);
        var result = new List<SubmissionSummaryAttachment>();
        foreach (var attachment in attachments)
        {
            if (!visibility.IsVisible(attachment.ComponentId))
                continue;
            var label = labels.GetValueOrDefault(attachment.ComponentId, "Attachment");
            result.Add(new SubmissionSummaryAttachment(label, attachment.FileName));
        }
        return result;
    }

    private static Dictionary<(string, string?), string> BuildMaskLookup(IReadOnlyCollection<SensitiveMask> masks)
    {
        var lookup = new Dictionary<(string, string?), string>();
        foreach (var mask in masks)
            lookup[(mask.ComponentId, mask.RowId)] = mask.Last4;
        return lookup;
    }

    private static string RowLabel(FormRepeat? repeat, int index)
    {
        var baseLabel = string.IsNullOrWhiteSpace(repeat?.RowLabel) ? "Row" : repeat!.RowLabel.Trim();
        return $"{baseLabel} {index.ToString(CultureInfo.InvariantCulture)}";
    }

    private static string Label(FormComponent component)
    {
        if (!string.IsNullOrWhiteSpace(component.Label))
            return component.Label.Trim();
        return string.IsNullOrWhiteSpace(component.Key) ? component.Id : component.Key.Trim();
    }

    private static string? PropString(FormComponent component, string key) =>
        component.Props.ValueKind == JsonValueKind.Object
        && component.Props.TryGetProperty(key, out var p)
        && p.ValueKind == JsonValueKind.String
        && !string.IsNullOrWhiteSpace(p.GetString())
            ? p.GetString()!.Trim()
            : null;

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Truncate(string value) => value.Length <= MaxValueChars ? value : value[..MaxValueChars];
}
