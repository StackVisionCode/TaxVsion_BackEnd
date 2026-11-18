using TaxVision.Customer.Domain.Customers;
using TaxVision.Customer.Domain.Customers.ValueObjects;
using TaxVision.Customer.Domain.Relations;
using DomainCustomer = TaxVision.Customer.Domain.Customers.Customer;

namespace TaxVision.Customer.Tests.Domain;

/// <summary>
/// El cónyuge recién agregado puede llevar su perfil fiscal antes de guardarse: la relación aún no tiene Id
/// (lo genera la base) y el perfil se enlaza por la navegación.
/// </summary>
public sealed class CustomerRelationTests
{
    private static readonly Guid ByUser = Guid.NewGuid();

    [Fact]
    public void A_new_spouse_keeps_its_fiscal_profile_before_saving()
    {
        var customer = DomainCustomer
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
        var spouse = customer
            .AddRelation(
                RelationshipKind.Spouse,
                RelationPurpose.TaxHouseholdMember,
                PersonalName.Create("William", "King").Value,
                null,
                null,
                null,
                null,
                ByUser
            )
            .Value;

        var result = spouse.SetFiscalProfile(
            TaxRelationshipRole.Spouse,
            [1, 2, 3],
            "blind-spouse",
            "4321",
            2026,
            qualifiesAsDependent: false,
            livedWithTaxpayer: true,
            ByUser
        );

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Equal("4321", spouse.FiscalProfile!.TaxIdentifierLast4);
    }
}
