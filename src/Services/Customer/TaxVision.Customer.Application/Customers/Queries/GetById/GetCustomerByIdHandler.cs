using BuildingBlocks.Results;
using TaxVision.Customer.Application.Abstractions;
using TaxVision.Customer.Application.Customers;

namespace TaxVision.Customer.Application.Customers.Queries.GetById;

public static class GetCustomerByIdHandler
{
    public static async Task<Result<CustomerResponse>> Handle(
        GetCustomerByIdQuery query,
        ICustomerReadService reader,
        CancellationToken ct
    )
    {
        var customer = await reader.GetByIdAsync(query.CustomerId, ct);
        return customer is null
            ? Result.Failure<CustomerResponse>(new Error("Customer.NotFound", "Customer not found."))
            : Result.Success(customer);
    }
}
