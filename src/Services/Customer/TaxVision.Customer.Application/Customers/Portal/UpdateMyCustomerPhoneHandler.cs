using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Customer.Application.Abstractions;
using TaxVision.Customer.Domain.Customers;
using TaxVision.Customer.Domain.Customers.ValueObjects;
using Wolverine;

namespace TaxVision.Customer.Application.Customers.Portal;

/// <summary>
/// El cliente cambia (o quita, si viene vacío) el teléfono principal de su ficha desde el portal, sin OTP.
/// Se publica CustomerUpdated igual que una edición del preparador, así las proyecciones se enteran.
/// </summary>
public static class UpdateMyCustomerPhoneHandler
{
    public static async Task<Result> Handle(
        UpdateMyCustomerPhoneCommand command,
        ICustomerRepository repository,
        IMessageBus bus,
        ICorrelationContext correlation,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var customer = await repository.GetByIdAsync(command.CustomerId, ct);
        if (customer is null || customer.TenantId != command.TenantId || customer.Status == CustomerStatus.Archived)
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
        await bus.PublishAsync(CustomerUpdatedEvents.From(customer, command.UserId, correlation.CorrelationId));
        return Result.Success();
    }
}
