using TaxVision.Customer.Application.Abstractions;

namespace TaxVision.Customer.Application.Customers.Queries.Overview;

public sealed record CustomerOverviewQuery(Guid TenantId, int Months);

public static class CustomerOverviewHandler
{
    public static Task<CustomerDirectoryOverviewResponse> Handle(
        CustomerOverviewQuery query,
        ICustomerReadService reader,
        CancellationToken ct
    ) => reader.GetOverviewAsync(query.TenantId, query.Months, ct);
}
