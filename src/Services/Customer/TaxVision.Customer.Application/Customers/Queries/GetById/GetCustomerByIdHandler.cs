using BuildingBlocks.Results;
using Microsoft.Extensions.Options;
using TaxVision.Customer.Application.Abstractions;
using TaxVision.Customer.Application.Customers;

namespace TaxVision.Customer.Application.Customers.Queries.GetById;

public static class GetCustomerByIdHandler
{
    public static async Task<Result<CustomerDetailResponse>> Handle(
        GetCustomerByIdQuery query,
        ICustomerReadService reader,
        IOptions<CustomerVisibilityOptions> visibility,
        CancellationToken ct
    )
    {
        var assignedTo = visibility.Value.Enabled && !query.CanViewAll ? query.ActorUserId : (Guid?)null;
        // includeAssignees = CanViewAll: la lista de asignados es solo para admin/view_all (need-to-know).
        var customer = await reader.GetDetailByIdAsync(
            query.TenantId,
            query.CustomerId,
            assignedTo,
            query.CanViewAll,
            ct
        );
        return customer is null
            ? Result.Failure<CustomerDetailResponse>(new Error("Customer.NotFound", "Customer not found."))
            : Result.Success(customer);
    }
}
