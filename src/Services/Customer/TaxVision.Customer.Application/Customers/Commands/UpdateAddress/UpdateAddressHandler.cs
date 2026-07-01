using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Customer.Application.Abstractions;
using TaxVision.Customer.Domain.Customers.ValueObjects;

namespace TaxVision.Customer.Application.Customers.Commands.UpdateAddress;

public static class UpdateAddressHandler
{
    public static async Task<Result> Handle(
        UpdateAddressCommand cmd,
        ICustomerRepository repository,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var customer = await repository.GetByIdAsync(cmd.CustomerId, ct);
        if (customer is null || customer.TenantId != cmd.TenantId)
            return Result.Failure(new Error("Customer.NotFound", "Customer not found."));

        var addressResult = AddressValue.Create(
            cmd.Line1,
            cmd.City,
            cmd.PostalCode,
            cmd.CountryCode,
            cmd.Line2,
            cmd.Region
        );
        if (addressResult.IsFailure)
            return addressResult;

        var updateResult = customer.UpdateAddress(
            cmd.AddressId,
            cmd.Kind,
            addressResult.Value,
            cmd.IsPrimary,
            cmd.ModifiedByUserId
        );
        if (updateResult.IsFailure)
            return updateResult;

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
