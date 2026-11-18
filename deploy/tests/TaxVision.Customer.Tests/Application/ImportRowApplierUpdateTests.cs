using TaxVision.Customer.Application.Abstractions;
using TaxVision.Customer.Application.Imports.Dtos;
using TaxVision.Customer.Application.Imports.Helpers;
using TaxVision.Customer.Application.Imports.Messages;
using TaxVision.Customer.Domain.Customers;
using TaxVision.Customer.Domain.Customers.ValueObjects;
using TaxVision.Customer.Domain.FiscalProfiles;
using TaxVision.Customer.Domain.Imports;
using DomainCustomer = TaxVision.Customer.Domain.Customers.Customer;

namespace TaxVision.Customer.Tests.Application;

/// <summary>
/// Merge solo completa lo vacío y Overwrite solo reemplaza lo que trae el archivo: ninguno borra el banco de
/// reembolso ni deja en blanco un dato porque la celda venga vacía.
/// </summary>
public sealed class ImportRowApplierUpdateTests
{
    private static readonly Guid Actor = Guid.NewGuid();
    private static readonly byte[] BankAccount = [7, 7, 7];
    private static readonly byte[] BankRouting = [8, 8, 8];

    [Fact]
    public void Merge_keeps_the_refund_bank_and_the_existing_fiscal_data()
    {
        var customer = ExistingWithFiscalProfile();

        var result = Apply(
            customer,
            Row(filingStatus: "Single", agi: "100", taxId: "123-45-6789"),
            DuplicateStrategy.Merge
        );

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        Assert.Equal(BankAccount, customer.FiscalProfile!.RefundBankAccountCipher);
        Assert.Equal(BankRouting, customer.FiscalProfile.RefundBankRoutingCipher);
        Assert.Equal(FilingStatus.MarriedJoint, customer.FiscalProfile.FilingStatus);
        Assert.Equal(50_000m, customer.FiscalProfile.PriorYearAgi);
    }

    [Fact]
    public void Merge_fills_fiscal_data_the_existing_client_does_not_have()
    {
        var customer = ExistingWithFiscalProfile(filingStatus: null, agi: null);

        Apply(customer, Row(filingStatus: "Single", agi: "100", taxId: "123-45-6789"), DuplicateStrategy.Merge);

        Assert.Equal(FilingStatus.Single, customer.FiscalProfile!.FilingStatus);
        Assert.Equal(100m, customer.FiscalProfile.PriorYearAgi);
        Assert.Equal(BankAccount, customer.FiscalProfile.RefundBankAccountCipher);
    }

    [Fact]
    public void Overwrite_replaces_the_fiscal_data_in_the_file_but_keeps_the_refund_bank()
    {
        var customer = ExistingWithFiscalProfile();

        var result = Apply(
            customer,
            Row(filingStatus: "Single", agi: "100", taxId: "123-45-6789"),
            DuplicateStrategy.Overwrite
        );

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        Assert.Equal(FilingStatus.Single, customer.FiscalProfile!.FilingStatus);
        Assert.Equal(100m, customer.FiscalProfile.PriorYearAgi);
        Assert.Equal(BankAccount, customer.FiscalProfile.RefundBankAccountCipher);
        Assert.Equal(BankRouting, customer.FiscalProfile.RefundBankRoutingCipher);
    }

    [Fact]
    public void Overwrite_with_blank_cells_does_not_erase_existing_data()
    {
        var occupation = Guid.NewGuid();
        var customer = ExistingWithFiscalProfile(language: Language.Es, occupationId: occupation);

        var result = Apply(customer, Row(filingStatus: "", agi: "", taxId: "123-45-6789"), DuplicateStrategy.Overwrite);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        Assert.Equal(Language.Es, customer.Language);
        Assert.Equal(occupation, customer.OccupationId);
        Assert.Equal(FilingStatus.MarriedJoint, customer.FiscalProfile!.FilingStatus);
        Assert.Equal(50_000m, customer.FiscalProfile.PriorYearAgi);
        Assert.True(customer.FiscalProfile.IsReturningCustomer);
    }

    private static BuildingBlocks.Results.Result Apply(
        DomainCustomer customer,
        ParsedRow row,
        DuplicateStrategy strategy
    ) =>
        ImportRowApplier.ApplyUpdate(
            customer,
            row,
            occupationId: null,
            businessIdentity: null,
            Actor,
            strategy,
            blindIndex: "blind-123456789",
            new FakeProtector()
        );

    private static ParsedRow Row(string filingStatus, string agi, string taxId) =>
        ImportRowParser
            .Parse(
                new ImportCustomerRow
                {
                    RowNumber = 2,
                    Kind = "Individual",
                    FirstName = "Ada",
                    LastName = "Lovelace",
                    Email = "ada@example.com",
                    TaxIdentifier = taxId,
                    FilingStatus = filingStatus,
                    PriorYearAgi = agi,
                }
            )
            .Value;

    private static DomainCustomer ExistingWithFiscalProfile(
        FilingStatus? filingStatus = FilingStatus.MarriedJoint,
        decimal? agi = 50_000m,
        Language language = Language.En,
        Guid? occupationId = null
    )
    {
        var customer = DomainCustomer
            .Register(
                Guid.NewGuid(),
                CustomerKind.Individual,
                PersonalName.Create("Ada", "Lovelace").Value,
                null,
                EmailAddress.Create("ada@example.com").Value,
                null,
                language,
                Actor
            )
            .Value;
        if (occupationId is not null)
            customer.ChangeOccupation(occupationId, Actor);
        customer.SetFiscalProfile(
            FiscalSubjectKind.Individual,
            [1, 2, 3],
            "blind-123456789",
            "6789",
            filingStatus,
            agi,
            isReturningCustomer: true,
            BankAccount,
            BankRouting,
            Actor
        );
        return customer;
    }

    private sealed class FakeProtector : ISensitiveDataProtector
    {
        public short CurrentKeyVersion => 1;

        public byte[] Protect(string plainText) => [1, 2, 3];

        public string Unprotect(byte[] cipher) => string.Empty;

        public string ComputeBlindIndex(string plainText, Guid tenantId) => $"blind-{plainText}";

        public IReadOnlyCollection<string> ComputeBlindIndexCandidates(string plainText, Guid tenantId) => [];
    }
}
