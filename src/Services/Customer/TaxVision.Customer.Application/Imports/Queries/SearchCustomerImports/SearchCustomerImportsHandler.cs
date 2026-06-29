using TaxVision.Customer.Application.Abstractions;
using TaxVision.Customer.Application.Imports.Dtos;

namespace TaxVision.Customer.Application.Imports.Queries.SearchCustomerImports;

public static class SearchCustomerImportsHandler
{
    public static Task<IReadOnlyList<CustomerImportAttemptResponse>> Handle(
        SearchCustomerImportsQuery query,
        ICustomerImportReadService reader,
        CancellationToken ct
    ) => reader.SearchAsync(query.TenantId, query.Page, query.Size, ct);
}
