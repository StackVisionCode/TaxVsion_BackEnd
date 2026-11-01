using System.Globalization;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.DataCollection.Engine;
using TaxVision.Tasks.ClientForms.Application.Common.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Templates.Abstractions;
using TaxVision.Tasks.ClientForms.Domain.Templates;

namespace TaxVision.Tasks.ClientForms.Application.Reporting;

public sealed record FormReportExportResult(
    string FileName,
    IReadOnlyList<string> Headers,
    IReadOnlyList<IReadOnlyList<string>> Rows
);

public static class FormReportExportHandler
{
    public const string Action = "report.exported";

    public static async Task<Result<FormReportExportResult>> Handle(
        FormReportExportQuery query,
        IFormTemplateVersionRepository versions,
        IFormEngine engine,
        IFormAnswerIndexReadRepository read,
        IFormAuditLog audit,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var schema = await FormReportTemplateSchema.LatestAsync(query.TenantId, query.TemplateId, versions, engine, ct);
        if (schema is null)
            return Result.Failure<FormReportExportResult>(FormTemplateErrors.NotFound);

        var entries = await read.ExportRowsAsync(query, ct);
        var columns = Columns(schema.Keys, entries);
        var headers = BaseHeaders.Concat(columns.Select(column => column.Label)).Select(FormReportCsv.Guard).ToArray();
        var rows = Pivot(entries, columns, schema.Labels);

        audit.RecordStaffOutcome(
            query.TenantId,
            query.TemplateId,
            null,
            Action,
            "success",
            query.ActorUserId,
            $"{{\"rows\":{rows.Count},\"from\":{DateJson(query.From)},\"to\":{DateJson(query.To)}}}"
        );
        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success(new FormReportExportResult(FileName(schema.Title), headers, rows));
    }

    private static readonly string[] BaseHeaders = ["Client", "Form", "Completed at", "Revision"];

    private sealed record Column(string Key, string Label);

    // Columnas = claves reportables de la última versión, más cualquier clave extra presente en los
    // datos (de versiones anteriores) que no esté en ese conjunto, para no perder valores.
    private static IReadOnlyList<Column> Columns(
        IReadOnlyList<FormReportableKey> keys,
        IReadOnlyList<FormReportExportEntry> entries
    )
    {
        var columns = keys.Select(key => new Column(key.Key, key.Label)).ToList();
        var known = new HashSet<string>(columns.Select(column => column.Key), StringComparer.Ordinal);
        foreach (
            var extra in entries
                .Select(entry => entry.Key)
                .Where(key => known.Add(key))
                .Distinct()
                .OrderBy(key => key, StringComparer.Ordinal)
        )
            columns.Add(new Column(extra, extra));
        return columns;
    }

    private static IReadOnlyList<IReadOnlyList<string>> Pivot(
        IReadOnlyList<FormReportExportEntry> entries,
        IReadOnlyList<Column> columns,
        FormReportLabels labels
    )
    {
        var rows = new List<IReadOnlyList<string>>();
        var groups = entries
            .GroupBy(entry => entry.FormRequestId)
            .OrderByDescending(group => group.First().CompletedAtUtc)
            .ThenBy(group => group.First().DisplayName, StringComparer.Ordinal);
        foreach (var group in groups)
        {
            var head = group.First();
            var cells = new List<string?>
            {
                head.DisplayName,
                head.Title,
                head.CompletedAtUtc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
                head.RevisionNumber.ToString(CultureInfo.InvariantCulture),
            };
            foreach (var column in columns)
            {
                var values = group
                    .Where(entry => string.Equals(entry.Key, column.Key, StringComparison.Ordinal))
                    .OrderBy(entry => entry.RowIndex ?? 0)
                    .Select(entry => labels.Of(entry.VersionNumber, entry.Key, entry.ValueText));
                cells.Add(string.Join("; ", values));
            }
            rows.Add(FormReportCsv.GuardRow(cells));
        }
        return rows;
    }

    private static string DateJson(DateOnly? date) =>
        date is null ? "null" : $"\"{date.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}\"";

    private static string FileName(string title)
    {
        var safe = new string(
            title
                .Select(character => char.IsLetterOrDigit(character) || character is ' ' or '-' ? character : '_')
                .ToArray()
        ).Trim();
        if (string.IsNullOrEmpty(safe))
            safe = "form";
        return $"{safe} report.csv";
    }
}
