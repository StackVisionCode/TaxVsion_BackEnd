using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Customer.Application.Abstractions;
using TaxVision.Customer.Domain.ContactPoints;
using TaxVision.Customer.Domain.Customers.ValueObjects;

namespace TaxVision.Customer.Application.Customers.Commands.AddContactPoint;

public static class AddContactPointHandler
{
    public static async Task<Result<ContactPointResponse>> Handle(
        AddContactPointCommand cmd,
        ICustomerRepository repository,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var customer = await repository.GetByIdAsync(cmd.CustomerId, ct);
        if (customer is null || customer.TenantId != cmd.TenantId)
            return Result.Failure<ContactPointResponse>(new Error("Customer.NotFound", "Customer not found."));

        string normalizedValue;
        string finalValue;

        if (cmd.Type == ContactPointType.Email)
        {
            var emailResult = EmailAddress.Create(cmd.Value);
            if (emailResult.IsFailure)
                return Result.Failure<ContactPointResponse>(emailResult.Error);
            finalValue = emailResult.Value.Value;
            normalizedValue = emailResult.Value.NormalizedValue;
        }
        else
        {
            var phoneResult = PhoneNumber.Create(cmd.Value);
            if (phoneResult.IsFailure)
                return Result.Failure<ContactPointResponse>(phoneResult.Error);
            finalValue = phoneResult.Value.E164Value;
            normalizedValue = phoneResult.Value.E164Value;
        }

        var addResult = customer.AddContactPoint(
            cmd.Type,
            finalValue,
            normalizedValue,
            cmd.Label,
            cmd.IsPrimary,
            cmd.ModifiedByUserId
        );
        if (addResult.IsFailure)
            return Result.Failure<ContactPointResponse>(addResult.Error);

        await unitOfWork.SaveChangesAsync(ct);

        var c = addResult.Value;
        return Result.Success(new ContactPointResponse(c.Id, c.Type, c.Value, c.Label, c.IsPrimary, c.VerifiedAtUtc));
    }
}
