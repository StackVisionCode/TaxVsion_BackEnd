using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CustomerIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Customer.Application.Abstractions;
using TaxVision.Customer.Domain.Customers;
using TaxVision.Customer.Domain.Customers.ValueObjects;
using Wolverine;
using CustomerEntity = TaxVision.Customer.Domain.Customers.Customer;

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

        var voResult = BuildContactValueObjects(cmd);
        if (voResult.IsFailure)
            return Result.Failure<CustomerResponse>(voResult.Error);

        var applyResult = ApplyChanges(customer, cmd, voResult.Value);
        if (applyResult.IsFailure)
            return Result.Failure<CustomerResponse>(applyResult.Error);

        await unitOfWork.SaveChangesAsync(ct);
        await PublishUpdatedEventAsync(customer, cmd, correlation, bus);

        return Result.Success(MapToResponse(customer));
    }

    // ============== Fase 1: construir value objects de contacto (email/phone) ==============

    private sealed record ContactValueObjects(EmailAddress Email, PhoneNumber? Phone);

    private static Result<ContactValueObjects> BuildContactValueObjects(UpdateCustomerCommand cmd)
    {
        var emailResult = EmailAddress.Create(cmd.PrimaryEmail);
        if (emailResult.IsFailure)
            return Result.Failure<ContactValueObjects>(emailResult.Error);

        PhoneNumber? phone = null;
        if (!string.IsNullOrWhiteSpace(cmd.PrimaryPhone))
        {
            var phoneResult = PhoneNumber.Create(cmd.PrimaryPhone);
            if (phoneResult.IsFailure)
                return Result.Failure<ContactValueObjects>(phoneResult.Error);
            phone = phoneResult.Value;
        }

        return Result.Success(new ContactValueObjects(emailResult.Value, phone));
    }

    // ============== Fase 2: aplicar cambios al aggregate ==============

    private static Result ApplyChanges(CustomerEntity customer, UpdateCustomerCommand cmd, ContactValueObjects vo)
    {
        var results = new[]
        {
            customer.ChangePreferences(cmd.Language, cmd.PreferredChannel, cmd.ModifiedByUserId),
            customer.ChangeOccupation(cmd.OccupationId, cmd.ModifiedByUserId),
            customer.SetProfilePicture(cmd.ProfilePictureFileId, cmd.ModifiedByUserId),
            customer.ChangePrimaryEmail(vo.Email, cmd.ModifiedByUserId),
            customer.ChangePrimaryPhone(vo.Phone, cmd.ModifiedByUserId),
        };

        foreach (var r in results)
            if (r.IsFailure)
                return r;

        var identityResult = ApplyIdentityChanges(customer, cmd);
        if (identityResult.IsFailure)
            return identityResult;

        if (cmd.DateOfBirth.HasValue)
            return customer.ChangeDateOfBirth(cmd.DateOfBirth, cmd.ModifiedByUserId);

        return Result.Success();
    }

    // ============== Fase 3: aplicar cambios de identidad (name / business) ==============

    private static Result ApplyIdentityChanges(CustomerEntity customer, UpdateCustomerCommand cmd)
    {
        if (customer.Kind == CustomerKind.Individual && HasPersonalNameChanges(cmd))
            return ApplyPersonalNameChange(customer, cmd);

        if (customer.Kind == CustomerKind.Business && HasBusinessIdentityChanges(cmd))
            return ApplyBusinessIdentityChange(customer, cmd);

        return Result.Success();
    }

    private static bool HasPersonalNameChanges(UpdateCustomerCommand cmd) =>
        !string.IsNullOrWhiteSpace(cmd.FirstName)
        || !string.IsNullOrWhiteSpace(cmd.LastName)
        || !string.IsNullOrWhiteSpace(cmd.MiddleName)
        || !string.IsNullOrWhiteSpace(cmd.Prefix)
        || !string.IsNullOrWhiteSpace(cmd.Suffix);

    private static bool HasBusinessIdentityChanges(UpdateCustomerCommand cmd) =>
        !string.IsNullOrWhiteSpace(cmd.LegalName)
        || !string.IsNullOrWhiteSpace(cmd.Dba)
        || cmd.BusinessStructure.HasValue
        || cmd.FormationDate.HasValue
        || cmd.PrincipalBusinessActivityId.HasValue;

    private static Result ApplyPersonalNameChange(CustomerEntity customer, UpdateCustomerCommand cmd)
    {
        // Fusionar: campos vacios del comando conservan el valor actual del aggregate
        var current = customer.PersonalName;
        var nameResult = PersonalName.Create(
            firstName: cmd.FirstName ?? current?.FirstName ?? string.Empty,
            lastName: cmd.LastName ?? current?.LastName ?? string.Empty,
            middleName: cmd.MiddleName ?? current?.MiddleName,
            prefix: cmd.Prefix ?? current?.Prefix,
            suffix: cmd.Suffix ?? current?.Suffix
        );
        if (nameResult.IsFailure)
            return nameResult;

        return customer.ChangePersonalName(nameResult.Value, cmd.ModifiedByUserId);
    }

    private static Result ApplyBusinessIdentityChange(CustomerEntity customer, UpdateCustomerCommand cmd)
    {
        var current = customer.BusinessIdentity;
        var bizResult = BusinessIdentity.Create(
            legalName: cmd.LegalName ?? current?.LegalName ?? string.Empty,
            structure: cmd.BusinessStructure ?? current?.Structure ?? Domain.Customers.BusinessStructure.Other,
            dba: cmd.Dba ?? current?.Dba,
            formationDate: cmd.FormationDate ?? current?.FormationDate,
            principalBusinessActivityId: cmd.PrincipalBusinessActivityId ?? current?.PrincipalBusinessActivityId
        );
        if (bizResult.IsFailure)
            return bizResult;

        return customer.ChangeBusinessIdentity(bizResult.Value, cmd.ModifiedByUserId);
    }

    // ============== Fase 4: publicar evento de update ==============

    private static Task PublishUpdatedEventAsync(
        CustomerEntity customer,
        UpdateCustomerCommand cmd,
        ICorrelationContext correlation,
        IMessageBus bus
    ) =>
        bus.PublishAsync(
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
            )
            .AsTask();

    // ============== Mapeo ==============

    private static CustomerResponse MapToResponse(CustomerEntity c) =>
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
