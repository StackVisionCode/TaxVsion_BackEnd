using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CustomerIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Customer.Application.Abstractions;
using TaxVision.Customer.Domain.Customers.ValueObjects;
using Wolverine;

namespace TaxVision.Customer.Application.Customers.Commands.Update;

public static class UpdateCustomerHandler
{
    public static async Task<Result<CustomerResponse>> Handle(
        UpdateCustomerCommand cmd,
        ICustomerRepository repository,
        IMessageBus bus,
        ICorrelationContext correlation,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var customer = await repository.GetByIdAsync(cmd.CustomerId, ct);
        if (customer is null || customer.TenantId != cmd.TenantId)
            return Result.Failure<CustomerResponse>(new Error("Customer.NotFound", "Customer not found."));

        var emailResult = EmailAddress.Create(cmd.PrimaryEmail);
        if (emailResult.IsFailure)
            return Result.Failure<CustomerResponse>(emailResult.Error);

        PhoneNumber? phone = null;
        if (!string.IsNullOrWhiteSpace(cmd.PrimaryPhone))
        {
            var phoneResult = PhoneNumber.Create(cmd.PrimaryPhone);
            if (phoneResult.IsFailure)
                return Result.Failure<CustomerResponse>(phoneResult.Error);
            phone = phoneResult.Value;
        }

        var prefResult = customer.ChangePreferences(cmd.Language, cmd.PreferredChannel, cmd.ModifiedByUserId);
        if (prefResult.IsFailure)
            return Result.Failure<CustomerResponse>(prefResult.Error);

        var occResult = customer.ChangeOccupation(cmd.OccupationId, cmd.ModifiedByUserId);
        if (occResult.IsFailure)
            return Result.Failure<CustomerResponse>(occResult.Error);

        var picResult = customer.SetProfilePicture(cmd.ProfilePictureFileId, cmd.ModifiedByUserId);
        if (picResult.IsFailure)
            return Result.Failure<CustomerResponse>(picResult.Error);

        var emailChangeResult = customer.ChangePrimaryEmail(emailResult.Value, cmd.ModifiedByUserId);
        if (emailChangeResult.IsFailure)
            return Result.Failure<CustomerResponse>(emailChangeResult.Error);

        var phoneChangeResult = customer.ChangePrimaryPhone(phone, cmd.ModifiedByUserId);
        if (phoneChangeResult.IsFailure)
            return Result.Failure<CustomerResponse>(phoneChangeResult.Error);

        await unitOfWork.SaveChangesAsync(ct);

        await bus.PublishAsync(
            new CustomerUpdatedIntegrationEvent
            {
                TenantId = customer.TenantId,
                CorrelationId = correlation.CorrelationId,
                CustomerId = customer.Id,
                DisplayName = customer.DisplayName,
                PrimaryEmail = customer.PrimaryEmail.Value,
                PrimaryPhone = customer.PrimaryPhone?.E164Value,
                Language = customer.Language.ToString(),
                PreferredChannel = customer.PreferredChannel.ToString(),
                OccupationId = customer.OccupationId,
                ModifiedByUserId = cmd.ModifiedByUserId,
            }
        );

        return Result.Success(
            new CustomerResponse(
                customer.Id,
                customer.TenantId,
                customer.Kind,
                customer.Status,
                customer.DisplayName,
                customer.PrimaryEmail.Value,
                customer.PrimaryPhone?.E164Value,
                customer.Language,
                customer.PreferredChannel,
                customer.OccupationId,
                OccupationName: null,
                customer.BusinessIdentity?.PrincipalBusinessActivityId,
                PrincipalBusinessActivityName: null,
                customer.CreatedAtUtc
            )
        );
    }
}
