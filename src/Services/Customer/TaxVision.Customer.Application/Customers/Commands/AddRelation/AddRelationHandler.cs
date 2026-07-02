using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Customer.Application.Abstractions;
using TaxVision.Customer.Domain.Customers.ValueObjects;

namespace TaxVision.Customer.Application.Customers.Commands.AddRelation;

public static class AddRelationHandler
{
    public static async Task<Result<RelationResponse>> Handle(
        AddRelationCommand cmd,
        ICustomerRepository repository,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var customer = await repository.GetByIdAsync(cmd.CustomerId, ct);
        if (customer is null)
            return Result.Failure<RelationResponse>(new Error("Customer.NotFound", "Customer not found."));

        var nameResult = PersonalName.Create(cmd.FirstName, cmd.LastName, cmd.MiddleName, cmd.Prefix, cmd.Suffix);
        if (nameResult.IsFailure)
            return Result.Failure<RelationResponse>(nameResult.Error);

        EmailAddress? email = null;
        if (!string.IsNullOrWhiteSpace(cmd.PrimaryEmail))
        {
            var emailResult = EmailAddress.Create(cmd.PrimaryEmail);
            if (emailResult.IsFailure)
                return Result.Failure<RelationResponse>(emailResult.Error);
            email = emailResult.Value;
        }

        PhoneNumber? phone = null;
        if (!string.IsNullOrWhiteSpace(cmd.PrimaryPhone))
        {
            var phoneResult = PhoneNumber.Create(cmd.PrimaryPhone);
            if (phoneResult.IsFailure)
                return Result.Failure<RelationResponse>(phoneResult.Error);
            phone = phoneResult.Value;
        }

        AddressValue? address = null;
        if (!string.IsNullOrWhiteSpace(cmd.AddressLine1))
        {
            var addressResult = AddressValue.Create(
                cmd.AddressLine1,
                cmd.AddressCity ?? "",
                cmd.AddressPostalCode ?? "",
                cmd.AddressCountryCode ?? "",
                cmd.AddressLine2,
                cmd.AddressRegion
            );
            if (addressResult.IsFailure)
                return Result.Failure<RelationResponse>(addressResult.Error);
            address = addressResult.Value;
        }

        var addResult = customer.AddRelation(
            cmd.RelationshipKind,
            cmd.Purposes,
            nameResult.Value,
            email,
            phone,
            cmd.DateOfBirth,
            address,
            cmd.ModifiedByUserId
        );
        if (addResult.IsFailure)
            return Result.Failure<RelationResponse>(addResult.Error);

        await unitOfWork.SaveChangesAsync(ct);

        var r = addResult.Value;
        return Result.Success(
            new RelationResponse(
                r.Id,
                r.RelationshipKind,
                r.Purposes,
                r.Name.DisplayName,
                r.PrimaryEmail?.Value,
                r.PrimaryPhone?.E164Value,
                r.DateOfBirth,
                r.IsActive
            )
        );
    }
}
