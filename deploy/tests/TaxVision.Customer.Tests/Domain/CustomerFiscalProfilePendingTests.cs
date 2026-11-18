using TaxVision.Customer.Domain.Customers;
using TaxVision.Customer.Domain.Customers.ValueObjects;
using TaxVision.Customer.Domain.FiscalProfiles;
using DomainCustomer = TaxVision.Customer.Domain.Customers.Customer;

namespace TaxVision.Customer.Tests.Domain;

/// <summary>
/// D-4: el perfil fiscal (filing status, AGI, cliente recurrente) puede existir con el SSN/EIN pendiente, y el
/// identificador se agrega después sin perder esos datos.
/// </summary>
public sealed class CustomerFiscalProfilePendingTests
{
    private static readonly Guid ByUser = Guid.NewGuid();

    [Fact]
    public void A_fiscal_profile_can_start_without_a_tax_identifier()
    {
        var customer = NewCustomer();

        var result = customer.SetFiscalProfileWithoutTaxIdentifier(
            FiscalSubjectKind.Individual,
            FilingStatus.Single,
            45_000m,
            isReturningCustomer: true,
            ByUser
        );

        Assert.True(result.IsSuccess);
        var profile = customer.FiscalProfile!;
        Assert.False(profile.HasTaxIdentifier);
        Assert.Null(profile.TaxIdentifierCipher);
        Assert.Null(profile.TaxIdentifierBlindIndex);
        Assert.Null(profile.TaxIdentifierLast4);
        Assert.Equal(FilingStatus.Single, profile.FilingStatus);
        Assert.Equal(45_000m, profile.PriorYearAgi);
        Assert.True(profile.IsReturningCustomer);
    }

    [Fact]
    public void Without_a_tax_identifier_it_keeps_the_one_already_on_file()
    {
        var customer = NewCustomer();
        customer.SetFiscalProfile(
            FiscalSubjectKind.Individual,
            [1, 2, 3],
            "blind",
            "6789",
            null,
            null,
            false,
            null,
            null,
            ByUser
        );

        customer.SetFiscalProfileWithoutTaxIdentifier(
            FiscalSubjectKind.Individual,
            FilingStatus.HeadOfHousehold,
            null,
            false,
            ByUser
        );

        Assert.Equal("6789", customer.FiscalProfile!.TaxIdentifierLast4);
        Assert.Equal(FilingStatus.HeadOfHousehold, customer.FiscalProfile.FilingStatus);
    }

    [Fact]
    public void The_tax_identifier_can_be_added_later()
    {
        var customer = NewCustomer();
        customer.SetFiscalProfileWithoutTaxIdentifier(
            FiscalSubjectKind.Individual,
            FilingStatus.Single,
            null,
            false,
            ByUser
        );

        var result = customer.SetFiscalProfile(
            FiscalSubjectKind.Individual,
            [1, 2, 3],
            "blind",
            "6789",
            FilingStatus.Single,
            null,
            false,
            null,
            null,
            ByUser
        );

        Assert.True(result.IsSuccess);
        Assert.True(customer.FiscalProfile!.HasTaxIdentifier);
        Assert.Equal("6789", customer.FiscalProfile.TaxIdentifierLast4);
    }

    private static DomainCustomer NewCustomer() =>
        DomainCustomer
            .Register(
                Guid.NewGuid(),
                CustomerKind.Individual,
                PersonalName.Create("Ada", "Lovelace").Value,
                null,
                EmailAddress.Create("ada@example.com").Value,
                null,
                Language.En,
                ByUser
            )
            .Value;
}
