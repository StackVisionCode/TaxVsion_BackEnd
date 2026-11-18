using TaxVision.Customer.Domain.Customers;
using TaxVision.Customer.Domain.Customers.Events;
using TaxVision.Customer.Domain.Customers.ValueObjects;
using DomainCustomer = TaxVision.Customer.Domain.Customers.Customer;
using FiscalProfiles = TaxVision.Customer.Domain.FiscalProfiles;

namespace TaxVision.Customer.Tests.Domain;

/// <summary>
/// D-3: un Individual puede no tener email si se le puede ubicar por teléfono o fecha de nacimiento; un Business,
/// por teléfono o EIN. La regla se cumple al crear y también al quitar el teléfono o la fecha.
/// </summary>
public sealed class CustomerOptionalEmailTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid ByUser = Guid.NewGuid();
    private static readonly PhoneNumber Phone = PhoneNumber.Create("+13055551234").Value;
    private static readonly DateOnly Birth = new(1980, 5, 1);

    [Fact]
    public void An_individual_with_a_phone_can_be_created_without_email()
    {
        var result = Individual(phone: Phone);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.PrimaryEmail);
    }

    [Fact]
    public void An_individual_with_a_date_of_birth_can_be_created_without_email()
    {
        Assert.True(Individual(dateOfBirth: Birth).IsSuccess);
    }

    [Fact]
    public void An_individual_without_email_phone_or_date_of_birth_is_rejected()
    {
        Assert.Equal("Customer.ContactRequired", Individual().Error.Code);
    }

    [Fact]
    public void A_business_with_a_phone_can_be_created_without_email()
    {
        var result = Business(phone: Phone);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.PrimaryEmail);
    }

    // El import trae el EIN y lo fija en la misma transacción del alta.
    [Fact]
    public void A_business_with_an_ein_can_be_created_without_email()
    {
        Assert.True(Business(withTaxIdentifier: true).IsSuccess);
    }

    [Fact]
    public void A_business_without_email_phone_or_ein_is_rejected()
    {
        var result = Business();

        Assert.Equal("Customer.ContactRequired", result.Error.Code);
        Assert.Contains("EIN", result.Error.Message);
    }

    [Fact]
    public void Without_email_a_business_keeps_its_phone_unless_it_has_an_ein()
    {
        var company = Business(phone: Phone).Value;
        Assert.Equal("Customer.ContactRequired", company.ChangePrimaryPhone(null, ByUser).Error.Code);

        company.SetFiscalProfile(
            FiscalProfiles.FiscalSubjectKind.Business,
            [1],
            "blind",
            "6789",
            null,
            null,
            false,
            null,
            null,
            ByUser
        );

        Assert.True(company.ChangePrimaryPhone(null, ByUser).IsSuccess);
    }

    [Fact]
    public void Without_email_the_phone_cannot_be_removed_unless_there_is_a_date_of_birth()
    {
        var onlyPhone = Individual(phone: Phone).Value;
        Assert.Equal("Customer.ContactRequired", onlyPhone.ChangePrimaryPhone(null, ByUser).Error.Code);
        Assert.Equal(Phone, onlyPhone.PrimaryPhone);

        var withBirth = Individual(phone: Phone, dateOfBirth: Birth).Value;
        Assert.True(withBirth.ChangePrimaryPhone(null, ByUser).IsSuccess);
    }

    [Fact]
    public void Without_email_the_date_of_birth_cannot_be_removed_unless_there_is_a_phone()
    {
        var onlyBirth = Individual(dateOfBirth: Birth).Value;

        Assert.Equal("Customer.ContactRequired", onlyBirth.ChangeDateOfBirth(null, ByUser).Error.Code);
        Assert.Equal(Birth, onlyBirth.DateOfBirth);
    }

    [Fact]
    public void An_email_added_later_is_announced()
    {
        var customer = Individual(phone: Phone).Value;
        customer.ClearDomainEvents();

        var result = customer.ChangePrimaryEmail(EmailAddress.Create("late@example.com").Value, ByUser);

        Assert.True(result.IsSuccess);
        Assert.Equal("late@example.com", customer.PrimaryEmail!.Value);
        Assert.Single(customer.DomainEvents.OfType<CustomerUpdatedDomainEvent>());
        Assert.True(customer.ChangePrimaryPhone(null, ByUser).IsSuccess);
    }

    private static BuildingBlocks.Results.Result<DomainCustomer> Business(
        PhoneNumber? phone = null,
        bool withTaxIdentifier = false
    ) =>
        DomainCustomer.Register(
            Tenant,
            CustomerKind.Business,
            null,
            BusinessIdentity.Create("Acme LLC", BusinessStructure.Llc).Value,
            null,
            phone,
            Language.En,
            ByUser,
            withTaxIdentifier: withTaxIdentifier
        );

    private static BuildingBlocks.Results.Result<DomainCustomer> Individual(
        PhoneNumber? phone = null,
        DateOnly? dateOfBirth = null
    ) =>
        DomainCustomer.Register(
            Tenant,
            CustomerKind.Individual,
            PersonalName.Create("Ada", "Lovelace").Value,
            null,
            null,
            phone,
            Language.En,
            ByUser,
            dateOfBirth
        );
}
