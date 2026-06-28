using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CustomerIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Customer.Application.Abstractions;
using TaxVision.Customer.Domain.Customers.ValueObjects;
using Wolverine;

namespace TaxVision.Customer.Application.Customers.Commands.Create;

public static class CreateCustomerHandler
{
    public static async Task<Result<CustomerResponse>> Handle(
        CreateCustomerCommand cmd,
        ICustomerRepository repository,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
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

        PersonalName? personalName = null;
        if (cmd.Kind == TaxVision.Customer.Domain.Customers.CustomerKind.Individual)
        {
            var nameResult = PersonalName.Create(
                cmd.FirstName ?? "",
                cmd.LastName ?? "",
                cmd.MiddleName,
                cmd.Prefix,
                cmd.Suffix
            );
            if (nameResult.IsFailure)
                return Result.Failure<CustomerResponse>(nameResult.Error);
            personalName = nameResult.Value;
        }

        BusinessIdentity? businessIdentity = null;
        if (cmd.Kind == TaxVision.Customer.Domain.Customers.CustomerKind.Business)
        {
            if (cmd.BusinessStructure is null)
                return Result.Failure<CustomerResponse>(
                    new Error("Customer.BusinessStructure", "Business structure is required.")
                );

            var bizResult = BusinessIdentity.Create(
                cmd.LegalName ?? "",
                cmd.BusinessStructure.Value,
                cmd.Dba,
                cmd.FormationDate,
                cmd.PrincipalBusinessActivityId
            );
            if (bizResult.IsFailure)
                return Result.Failure<CustomerResponse>(bizResult.Error);
            businessIdentity = bizResult.Value;
        }

        var customerResult = TaxVision.Customer.Domain.Customers.Customer.Register(
            cmd.TenantId,
            cmd.Kind,
            personalName,
            businessIdentity,
            emailResult.Value,
            phone,
            cmd.Language,
            cmd.PreferredChannel,
            createdByUserId: cmd.CreatedByUserId,
            dateOfBirth: cmd.DateOfBirth,
            occupationId: cmd.OccupationId
        );

        if (customerResult.IsFailure)
            return Result.Failure<CustomerResponse>(customerResult.Error);

        var customer = customerResult.Value;

        await repository.AddAsync(customer, ct);
        await unitOfWork.SaveChangesAsync(ct);

        await bus.PublishAsync(
            new CustomerCreatedIntegrationEvent
            {
                TenantId = customer.TenantId,
                CorrelationId = correlation.CorrelationId,
                CustomerId = customer.Id,
                Kind = customer.Kind.ToString(),
                DisplayName = customer.DisplayName,
                PrimaryEmail = customer.PrimaryEmail.Value,
                PrimaryPhone = customer.PrimaryPhone?.E164Value,
                Language = customer.Language.ToString(),
                PreferredChannel = customer.PreferredChannel.ToString(),
                OccupationId = customer.OccupationId,
                CreatedByUserId = customer.CreatedByUserId,
            }
        );

        return Result.Success(MapToResponse(customer));
    }

    private static CustomerResponse MapToResponse(TaxVision.Customer.Domain.Customers.Customer c) =>
        new(
            c.Id,
            c.TenantId,
            c.Kind,
            c.Status,
            c.DisplayName,
            c.PrimaryEmail.Value,
            c.PrimaryPhone?.E164Value,
            c.Language,
            c.PreferredChannel,
            c.OccupationId,
            OccupationName: null,
            c.BusinessIdentity?.PrincipalBusinessActivityId,
            PrincipalBusinessActivityName: null,
            c.CreatedAtUtc
        );
}
