using Microsoft.Extensions.Options;
using TaxVision.Customer.Application.Abstractions;

namespace TaxVision.Customer.Application.Customers.Queries.Overview;

public sealed record CustomerOverviewQuery(Guid TenantId, int Months, Guid ActorUserId, bool CanViewAll);

public static class CustomerOverviewHandler
{
    public static Task<CustomerDirectoryOverviewResponse> Handle(
        CustomerOverviewQuery query,
        ICustomerReadService reader,
        IOptions<CustomerVisibilityOptions> visibility,
        CancellationToken ct
    )
    {
        var assignedTo = visibility.Value.Enabled && !query.CanViewAll ? query.ActorUserId : (Guid?)null;
        return reader.GetOverviewAsync(query.TenantId, query.Months, assignedTo, ct);
    }
}
