using TaxVision.Customer.Domain.Customers;
using TaxVision.Customer.Domain.Customers.ValueObjects;
using TaxVision.Customer.Domain.FiscalProfiles;
using DomainCustomer = TaxVision.Customer.Domain.Customers.Customer;

namespace TaxVision.Customer.Tests.Domain;

/// <summary>
/// `UpdateFiscalProfile` habilita editar filing/AGI/returning (y banco) sin re-teclear el SSN/EIN:
/// el identificador cifrado, su blind index y su last4 quedan INTACTOS, y el banco se conserva si no
/// se envían cifras nuevas (a diferencia de `SetFiscalProfile`, que reemplaza el perfil entero).
/// </summary>
public sealed class CustomerFiscalProfileUpdateTests
{
    private static readonly Guid ByUser = Guid.NewGuid();

    private static DomainCustomer NewCustomerWithProfile()
    {
        var name = PersonalName.Create("Grace", "Hopper").Value;
        var email = EmailAddress.Create($"grace-{Guid.NewGuid():N}@example.com").Value;
        var customer = DomainCustomer
            .Register(
                Guid.NewGuid(),
                CustomerKind.Individual,
                name,
                null,
                email,
                null,
                Language.En,
                PreferredChannel.Email,
                ByUser
            )
            .Value;

        customer.SetFiscalProfile(
            subjectKind: FiscalSubjectKind.Individual,
            taxIdentifierCipher: [1, 2, 3],
            taxIdentifierBlindIndex: "blind-index",
            taxIdentifierLast4: "6789",
            filingStatus: FilingStatus.Single,
            priorYearAgi: 42000m,
            isReturningCustomer: false,
            refundBankAccountCipher: [9, 9],
            refundBankRoutingCipher: [8, 8],
            byUserId: ByUser
        );

        return customer;
    }

    [Fact]
    public void UpdateFiscalProfile_keeps_the_identifier_and_bank_while_changing_filing()
    {
        var customer = NewCustomerWithProfile();

        var result = customer.UpdateFiscalProfile(
            filingStatus: FilingStatus.MarriedJoint,
            priorYearAgi: 50000m,
            isReturningCustomer: true,
            refundBankAccountCipher: null, // sin banco nuevo → conservar
            refundBankRoutingCipher: null,
            byUserId: ByUser
        );

        Assert.True(result.IsSuccess);
        var fp = customer.FiscalProfile!;
        // Campos actualizados
        Assert.Equal(FilingStatus.MarriedJoint, fp.FilingStatus);
        Assert.Equal(50000m, fp.PriorYearAgi);
        Assert.True(fp.IsReturningCustomer);
        // Identificador INTACTO
        Assert.Equal("6789", fp.TaxIdentifierLast4);
        Assert.Equal("blind-index", fp.TaxIdentifierBlindIndex);
        Assert.Equal(new byte[] { 1, 2, 3 }, fp.TaxIdentifierCipher);
        // Banco CONSERVADO (no se pasó cifra nueva)
        Assert.Equal(new byte[] { 9, 9 }, fp.RefundBankAccountCipher);
    }

    [Fact]
    public void UpdateFiscalProfile_replaces_the_bank_only_when_new_ciphers_are_given()
    {
        var customer = NewCustomerWithProfile();

        customer.UpdateFiscalProfile(
            FilingStatus.Single,
            42000m,
            false,
            refundBankAccountCipher: [7, 7],
            refundBankRoutingCipher: [6, 6],
            ByUser
        );

        Assert.Equal(new byte[] { 7, 7 }, customer.FiscalProfile!.RefundBankAccountCipher);
    }

    [Fact]
    public void UpdateFiscalProfile_fails_when_no_profile_exists()
    {
        var name = PersonalName.Create("New", "Client").Value;
        var email = EmailAddress.Create($"new-{Guid.NewGuid():N}@example.com").Value;
        var customer = DomainCustomer
            .Register(
                Guid.NewGuid(),
                CustomerKind.Individual,
                name,
                null,
                email,
                null,
                Language.En,
                PreferredChannel.Email,
                ByUser
            )
            .Value;

        var result = customer.UpdateFiscalProfile(FilingStatus.Single, null, false, null, null, ByUser);

        Assert.True(result.IsFailure);
        Assert.Equal("FiscalProfile.NotFound", result.Error.Code);
    }
}
