using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Customer.Application.Abstractions;
using TaxVision.Customer.Domain.Customers.ValueObjects;

namespace TaxVision.Customer.Application.Customers.Commands.UpdateRelation;

public static class UpdateRelationHandler
{
    public static async Task<Result> Handle(
        UpdateRelationCommand cmd,
        ICustomerRepository repository,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var customer = await repository.GetByIdAsync(cmd.CustomerId, ct);
        if (customer is null || customer.TenantId != cmd.TenantId)
            return Result.Failure(new Error("Customer.NotFound", "Customer not found."));

        var nameResult = PersonalName.Create(cmd.FirstName, cmd.LastName, cmd.MiddleName, cmd.Prefix, cmd.Suffix);
        if (nameResult.IsFailure)
            return nameResult;

        EmailAddress? email = null;
        if (!string.IsNullOrWhiteSpace(cmd.PrimaryEmail))
        {
            var emailRes = EmailAddress.Create(cmd.PrimaryEmail);
            if (emailRes.IsFailure)
                return emailRes;
            email = emailRes.Value;
        }

        PhoneNumber? phone = null;
        if (!string.IsNullOrWhiteSpace(cmd.PrimaryPhone))
        {
            var phoneRes = PhoneNumber.Create(cmd.PrimaryPhone);
            if (phoneRes.IsFailure)
                return phoneRes;
            phone = phoneRes.Value;
        }

        AddressValue? address = null;
        if (
            !string.IsNullOrWhiteSpace(cmd.AddressLine1)
            || !string.IsNullOrWhiteSpace(cmd.AddressCity)
            || !string.IsNullOrWhiteSpace(cmd.AddressPostalCode)
        )
        {
            var addrRes = AddressValue.Create(
                cmd.AddressLine1 ?? string.Empty,
                cmd.AddressCity ?? string.Empty,
                cmd.AddressPostalCode ?? string.Empty,
                string.IsNullOrWhiteSpace(cmd.AddressCountryCode) ? "US" : cmd.AddressCountryCode,
                cmd.AddressLine2,
                cmd.AddressRegion
            );
            if (addrRes.IsFailure)
                return addrRes;
            address = addrRes.Value;
        }

        var updateResult = customer.UpdateRelation(
            cmd.RelationId,
            cmd.RelationshipKind,
            cmd.Purposes,
            nameResult.Value,
            email,
            phone,
            cmd.DateOfBirth,
            address,
            cmd.ModifiedByUserId
        );
        if (updateResult.IsFailure)
            return updateResult;

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
