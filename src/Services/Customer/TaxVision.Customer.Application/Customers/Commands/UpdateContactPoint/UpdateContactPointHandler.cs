using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Customer.Application.Abstractions;
using TaxVision.Customer.Domain.ContactPoints;
using TaxVision.Customer.Domain.Customers.ValueObjects;

namespace TaxVision.Customer.Application.Customers.Commands.UpdateContactPoint;

public static class UpdateContactPointHandler
{
    public static async Task<Result> Handle(
        UpdateContactPointCommand cmd,
        ICustomerRepository repository,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var customer = await repository.GetByIdAsync(cmd.CustomerId, ct);
        if (
            customer is null
            || customer.TenantId != cmd.TenantId
            // Visibilidad por asignación: un cliente que no le toca se comporta como inexistente,
            // igual que en la lectura. Un 403 confirmaría que ese id existe.
            || !CustomerAccessPolicy.CanAccess(customer, cmd.ModifiedByUserId, cmd.CallerCanViewAllCustomers)
        )
            return Result.Failure(new Error("Customer.NotFound", "Customer not found."));

        string finalValue;
        string normalizedValue;

        if (cmd.Type == ContactPointType.Email)
        {
            var emailResult = EmailAddress.Create(cmd.Value);
            if (emailResult.IsFailure)
                return emailResult;
            finalValue = emailResult.Value.Value;
            normalizedValue = emailResult.Value.NormalizedValue;
        }
        else
        {
            var phoneResult = PhoneNumber.Create(cmd.Value);
            if (phoneResult.IsFailure)
                return phoneResult;
            finalValue = phoneResult.Value.E164Value;
            normalizedValue = phoneResult.Value.E164Value;
        }

        var updateResult = customer.UpdateContactPoint(
            cmd.ContactPointId,
            cmd.Type,
            finalValue,
            normalizedValue,
            cmd.Label,
            cmd.IsPrimary,
            cmd.ModifiedByUserId
        );
        if (updateResult.IsFailure)
            return updateResult;

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
