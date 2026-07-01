using BuildingBlocks.Common;
using TaxVision.Customer.Application.Abstractions;

namespace TaxVision.Customer.Application.Customers.Queries.Search;

public static class SearchCustomersHandler
{
    public static Task<PagedResult<CustomerSummaryResponse>> Handle(
        SearchCustomersQuery query,
        ICustomerReadService reader,
        CancellationToken ct
    ) => reader.SearchAsync(query.Term, query.Status, query.Page, query.Size, ct);
}
