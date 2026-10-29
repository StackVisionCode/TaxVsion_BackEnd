using BuildingBlocks.Results;
using TaxVision.Customer.Application.Abstractions;

namespace TaxVision.Customer.Application.Customers.Portal;

/// <summary>Teléfono y email de la ficha del propio cliente (la misma que edita su preparador).</summary>
public static class GetMyCustomerContactHandler
{
    public static async Task<Result<MyCustomerContactResponse>> Handle(
        GetMyCustomerContactQuery query,
        ICustomerRepository repository,
        CancellationToken ct
    )
    {
        var customer = await repository.GetByIdAsync(query.CustomerId, ct);
        if (customer is null || customer.TenantId != query.TenantId)
            return Result.Failure<MyCustomerContactResponse>(new Error("Customer.NotFound", "Customer not found."));

        return Result.Success(
            new MyCustomerContactResponse(customer.PrimaryPhone?.E164Value, customer.PrimaryEmail.Value)
        );
    }
}
