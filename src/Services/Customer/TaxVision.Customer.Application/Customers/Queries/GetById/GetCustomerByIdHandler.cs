using BuildingBlocks.Results;
using TaxVision.Customer.Application.Abstractions;
using TaxVision.Customer.Application.Customers;

namespace TaxVision.Customer.Application.Customers.Queries.GetById;

public static class GetCustomerByIdHandler
{
    public static async Task<Result<CustomerDetailResponse>> Handle(
        GetCustomerByIdQuery query,
        ICustomerReadService reader,
        CancellationToken ct
    )
    {
        var customer = await reader.GetDetailByIdAsync(query.TenantId, query.CustomerId, ct);
        return customer is null
            ? Result.Failure<CustomerDetailResponse>(new Error("Customer.NotFound", "Customer not found."))
            : Result.Success(customer);
    }
}
