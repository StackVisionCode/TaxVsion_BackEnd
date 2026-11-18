using TaxVision.Customer.Application.Abstractions;
using TaxVision.Customer.Application.Imports.Dtos;

namespace TaxVision.Customer.Application.Imports.Queries.SearchCustomerImports;

public static class SearchCustomerImportsHandler
{
    public const int MaxPageSize = 100;

    // Página desde 1 y tamaño entre 1 y 100: fuera de rango, SQL Server rechaza el OFFSET negativo.
    public static Task<IReadOnlyList<CustomerImportAttemptResponse>> Handle(
        SearchCustomerImportsQuery query,
        ICustomerImportReadService reader,
        CancellationToken ct
    ) => reader.SearchAsync(query.TenantId, Math.Max(1, query.Page), Math.Clamp(query.Size, 1, MaxPageSize), ct);
}
