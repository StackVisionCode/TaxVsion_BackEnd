using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Customer.Application.Abstractions;
using TaxVision.Customer.Domain.Customers;
using TaxVision.Customer.Domain.Customers.ValueObjects;

namespace TaxVision.Customer.Application.Customers.Portal;

/// <summary>
/// El cliente cambia (o quita, si viene vacío) el teléfono principal de su ficha desde el portal, sin OTP.
/// Sale CustomerUpdated (evento de dominio) igual que una edición del preparador, así las proyecciones se enteran.
/// </summary>
public static class UpdateMyCustomerPhoneHandler
{
    public static async Task<Result> Handle(
        UpdateMyCustomerPhoneCommand command,
        ICustomerRepository repository,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var customer = await repository.GetByIdAsync(command.TenantId, command.CustomerId, ct);
        if (customer is null || customer.Status == CustomerStatus.Archived)
            return Result.Failure(new Error("Customer.NotFound", "Customer not found."));

        PhoneNumber? phone = null;
        if (!string.IsNullOrWhiteSpace(command.PhoneNumber))
        {
            var parsed = PhoneNumber.Create(command.PhoneNumber);
            if (parsed.IsFailure)
                return Result.Failure(parsed.Error);
            phone = parsed.Value;
        }

        if (customer.PrimaryPhone == phone)
            return Result.Success();

        var changed = customer.ChangePrimaryPhone(phone, command.UserId);
        if (changed.IsFailure)
            return changed;

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
