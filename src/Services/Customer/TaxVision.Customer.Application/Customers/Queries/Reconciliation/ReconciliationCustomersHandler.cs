using BuildingBlocks.Common;
using TaxVision.Customer.Application.Abstractions;

namespace TaxVision.Customer.Application.Customers.Queries.Reconciliation;

public static class ReconciliationCustomersHandler
{
    public static Task<PagedResult<CustomerReconciliationResponse>> Handle(
        ReconciliationCustomersQuery query,
        ICustomerReadService reader,
        CancellationToken ct
    ) => reader.ListForReconciliationAsync(query.Status, query.Page, query.Size, ct);
}
