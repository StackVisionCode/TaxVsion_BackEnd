using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Customer.Application.Abstractions;
using TaxVision.Customer.Domain.Customers.ValueObjects;

namespace TaxVision.Customer.Application.Customers.Commands.AddAddress;

public static class AddAddressHandler
{
    public static async Task<Result<AddressResponse>> Handle(
        AddAddressCommand cmd,
        ICustomerRepository repository,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var customer = await repository.GetByIdAsync(cmd.CustomerId, ct);
        if (customer is null || customer.TenantId != cmd.TenantId)
            return Result.Failure<AddressResponse>(new Error("Customer.NotFound", "Customer not found."));

        var addressResult = AddressValue.Create(
            cmd.Line1,
            cmd.City,
            cmd.PostalCode,
            cmd.CountryCode,
            cmd.Line2,
            cmd.Region
        );
        if (addressResult.IsFailure)
            return Result.Failure<AddressResponse>(addressResult.Error);

        var addResult = customer.AddAddress(cmd.Kind, addressResult.Value, cmd.IsPrimary, cmd.ModifiedByUserId);
        if (addResult.IsFailure)
            return Result.Failure<AddressResponse>(addResult.Error);

        await unitOfWork.SaveChangesAsync(ct);

        var a = addResult.Value;
        return Result.Success(
            new AddressResponse(
                a.Id,
                a.Kind,
                a.Address.Line1,
                a.Address.Line2,
                a.Address.City,
                a.Address.Region,
                a.Address.PostalCode,
                a.Address.CountryCode,
                a.IsPrimary
            )
        );
    }
}
