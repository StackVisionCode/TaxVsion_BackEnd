using BuildingBlocks.Results;
using TaxVision.Customer.Domain.Customers;
using TaxVision.Customer.Domain.Customers.ValueObjects;
using CustomerEntity = TaxVision.Customer.Domain.Customers.Customer;

namespace TaxVision.Customer.Application.Customers.Commands.Create;

/// <summary>Los value objects del cliente, ya validados, armados desde el comando de alta.</summary>
internal sealed record CustomerDraft(
    EmailAddress? Email,
    PhoneNumber? Phone,
    PersonalName? PersonalName,
    BusinessIdentity? BusinessIdentity
)
{
    public Result<CustomerEntity> Register(CreateCustomerCommand cmd) =>
        CustomerEntity.Register(
            cmd.TenantId,
            cmd.Kind,
            PersonalName,
            BusinessIdentity,
            Email,
            Phone,
            cmd.Language,
            createdByUserId: cmd.CreatedByUserId,
            dateOfBirth: cmd.DateOfBirth,
            occupationId: cmd.OccupationId,
            contactPolicy: new ContactPolicy(cmd.EmailEnabled, cmd.SmsEnabled)
        );

    /// <summary>Le aplica estos datos al cliente que ya estaba. Devuelve el primer fallo.</summary>
    public Result ApplyTo(CustomerEntity existing, CreateCustomerCommand cmd)
    {
        var results = new List<Result>
        {
            existing.ChangePrimaryPhone(Phone, cmd.CreatedByUserId),
            existing.ChangeLanguage(cmd.Language, cmd.CreatedByUserId),
            existing.ChangeContactPolicy(cmd.EmailEnabled, cmd.SmsEnabled, cmd.CreatedByUserId),
            existing.ChangeOccupation(cmd.OccupationId, cmd.CreatedByUserId),
        };

        if (PersonalName is not null && existing.Kind == CustomerKind.Individual)
            results.Add(existing.ChangePersonalName(PersonalName, cmd.CreatedByUserId));

        if (BusinessIdentity is not null && existing.Kind == CustomerKind.Business)
            results.Add(existing.ChangeBusinessIdentity(BusinessIdentity, cmd.CreatedByUserId));

        if (cmd.DateOfBirth.HasValue)
            results.Add(existing.ChangeDateOfBirth(cmd.DateOfBirth, cmd.CreatedByUserId));

        // E-1: el email no se quita; sin email se conserva el que tenía.
        if (Email is not null)
            results.Insert(0, existing.ChangePrimaryEmail(Email, cmd.CreatedByUserId));

        foreach (var r in results)
            if (r.IsFailure)
                return r;

        return Result.Success();
    }
}

/// <summary>Arma el cliente desde el comando, antes de tomar el candado: un dato inválido no llega a la base.</summary>
internal static class CustomerDraftBuilder
{
    public static Result<CustomerDraft> Build(CreateCustomerCommand cmd)
    {
        // D-3: el email es opcional; la regla de contacto mínimo la decide el agregado al registrar.
        var emailResult = BuildEmail(cmd.PrimaryEmail);
        if (emailResult.IsFailure)
            return Result.Failure<CustomerDraft>(emailResult.Error);

        var phoneResult = BuildPhone(cmd.PrimaryPhone);
        if (phoneResult.IsFailure)
            return Result.Failure<CustomerDraft>(phoneResult.Error);

        var nameResult = BuildPersonalName(cmd);
        if (nameResult.IsFailure)
            return Result.Failure<CustomerDraft>(nameResult.Error);

        var businessResult = BuildBusinessIdentity(cmd);
        if (businessResult.IsFailure)
            return Result.Failure<CustomerDraft>(businessResult.Error);

        return Result.Success(
            new CustomerDraft(emailResult.Value, phoneResult.Value, nameResult.Value, businessResult.Value)
        );
    }

    private static Result<EmailAddress?> BuildEmail(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return Result.Success<EmailAddress?>(null);

        var emailResult = EmailAddress.Create(raw);
        return emailResult.IsFailure
            ? Result.Failure<EmailAddress?>(emailResult.Error)
            : Result.Success<EmailAddress?>(emailResult.Value);
    }

    private static Result<PhoneNumber?> BuildPhone(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return Result.Success<PhoneNumber?>(null);

        var phoneResult = PhoneNumber.Create(raw);
        return phoneResult.IsFailure
            ? Result.Failure<PhoneNumber?>(phoneResult.Error)
            : Result.Success<PhoneNumber?>(phoneResult.Value);
    }

    private static Result<PersonalName?> BuildPersonalName(CreateCustomerCommand cmd)
    {
        if (cmd.Kind != CustomerKind.Individual)
            return Result.Success<PersonalName?>(null);

        var nameResult = PersonalName.Create(
            cmd.FirstName ?? string.Empty,
            cmd.LastName ?? string.Empty,
            cmd.MiddleName,
            cmd.Prefix,
            cmd.Suffix
        );
        return nameResult.IsFailure
            ? Result.Failure<PersonalName?>(nameResult.Error)
            : Result.Success<PersonalName?>(nameResult.Value);
    }

    private static Result<BusinessIdentity?> BuildBusinessIdentity(CreateCustomerCommand cmd)
    {
        if (cmd.Kind != CustomerKind.Business)
            return Result.Success<BusinessIdentity?>(null);

        if (cmd.BusinessStructure is null)
            return Result.Failure<BusinessIdentity?>(
                new Error("Customer.BusinessStructure", "Business structure is required.")
            );

        var bizResult = BusinessIdentity.Create(
            cmd.LegalName ?? string.Empty,
            cmd.BusinessStructure.Value,
            cmd.Dba,
            cmd.FormationDate,
            cmd.PrincipalBusinessActivityId
        );
        return bizResult.IsFailure
            ? Result.Failure<BusinessIdentity?>(bizResult.Error)
            : Result.Success<BusinessIdentity?>(bizResult.Value);
    }
}
