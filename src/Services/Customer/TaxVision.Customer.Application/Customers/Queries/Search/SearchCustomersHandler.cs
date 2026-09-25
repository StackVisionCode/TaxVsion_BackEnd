using BuildingBlocks.Common;
using Microsoft.Extensions.Options;
using TaxVision.Customer.Application.Abstractions;

namespace TaxVision.Customer.Application.Customers.Queries.Search;

public static class SearchCustomersHandler
{
    public static Task<PagedResult<CustomerSummaryResponse>> Handle(
        SearchCustomersQuery query,
        ICustomerReadService reader,
        IOptions<CustomerVisibilityOptions> visibility,
        CancellationToken ct
    )
    {
        // Restringe por asignación solo con el flag encendido y sin view_all; si no, null = sin restricción.
        var assignedTo = visibility.Value.Enabled && !query.CanViewAll ? query.ActorUserId : (Guid?)null;
        // includeAssignees = CanViewAll: el roster de asignados es solo para admin/view_all (need-to-know).
        return reader.SearchAsync(
            query.TenantId,
            query.Term,
            query.Status,
            query.Page,
            query.Size,
            assignedTo,
            query.CanViewAll,
            ct
        );
    }
}
