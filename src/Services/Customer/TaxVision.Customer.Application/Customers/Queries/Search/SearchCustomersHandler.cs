using TaxVision.Customer.Application.Abstractions;

namespace TaxVision.Customer.Application.Customers.Queries.Search;

public static class SearchCustomersHandler
{
    public static async Task<IReadOnlyList<CustomerSummaryResponse>> Handle(
        SearchCustomersQuery q,
        ICustomerReadService reader,
        CancellationToken ct
    )
    {
        var page = q.Page < 1 ? 1 : q.Page;
        var size = q.Size switch
        {
            < 1 => 20,
            > 100 => 100,
            _ => q.Size,
        };

        return await reader.SearchAsync(q.Term, page, size, ct);
    }
}
