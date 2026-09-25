using BuildingBlocks.Common;
using TaxVision.Customer.Application.Abstractions;

namespace TaxVision.Customer.Application.Customers.Queries.Reconciliation;

public static class ReconcileAssignmentsHandler
{
    public static Task<PagedResult<CustomerAssignmentsReconciliationResponse>> Handle(
        ReconcileAssignmentsQuery query,
        ICustomerReadService reader,
        CancellationToken ct
    ) => reader.ListAssignmentsForReconciliationAsync(query.Page, query.Size, ct);
}
