using BuildingBlocks.Common;
using TaxVision.Tasks.ClientForms.Domain.Requests;

namespace TaxVision.Tasks.ClientForms.Application.Reporting;

/// <summary>Escritura del índice: solo agrega filas, que se confirman con la transacción de Complete.</summary>
public interface IFormAnswerIndexRepository
{
    void Add(FormAnswerIndexEntry entry);
}

/// <summary>Lectura del índice para los reportes. Aplica el ABAC del actor (solo clientes visibles).</summary>
public interface IFormAnswerIndexReadRepository
{
    Task<PagedResult<FormReportAnswerRow>> SearchAsync(FormReportAnswersQuery query, CancellationToken ct = default);

    Task<IReadOnlyList<FormReportSummaryBucket>> SummarizeAsync(
        FormReportSummaryQuery query,
        CancellationToken ct = default
    );

    Task<IReadOnlyList<FormReportExportEntry>> ExportRowsAsync(
        FormReportExportQuery query,
        CancellationToken ct = default
    );
}
