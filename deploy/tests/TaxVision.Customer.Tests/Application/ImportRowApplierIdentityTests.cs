using TaxVision.Customer.Application.Abstractions;
using TaxVision.Customer.Application.Imports.Dtos;
using TaxVision.Customer.Application.Imports.Helpers;
using TaxVision.Customer.Application.Imports.Messages;
using TaxVision.Customer.Domain.Addresses;
using TaxVision.Customer.Domain.Customers;
using TaxVision.Customer.Domain.Customers.ValueObjects;
using TaxVision.Customer.Domain.Imports;
using DomainCustomer = TaxVision.Customer.Domain.Customers.Customer;

namespace TaxVision.Customer.Tests.Application;

/// <summary>
/// Al actualizar un cliente existente, Overwrite pone la identidad del archivo (negocio, nombre, fecha, dirección) y
/// Merge solo completa lo vacío. Si nada cambia, la fila no cuenta como actualizada.
/// </summary>
public sealed class ImportRowApplierIdentityTests
{
    private static readonly Guid Actor = Guid.NewGuid();

    [Fact]
    public void Overwrite_replaces_the_business_identity_with_the_file_values()
    {
        var customer = ExistingBusiness(dba: null, formationDate: null);
        var row = BusinessRow(structure: "SCorp", dba: "Acme Roofs", formationDate: "2015-03-01");

        var result = Apply(customer, row, DuplicateStrategy.Overwrite);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        Assert.True(result.Value);
        Assert.Equal("Acme Roofs", customer.BusinessIdentity!.Dba);
        Assert.Equal(BusinessStructure.SCorp, customer.BusinessIdentity.Structure);
        Assert.Equal(new DateOnly(2015, 3, 1), customer.BusinessIdentity.FormationDate);
    }

    [Fact]
    public void Overwrite_keeps_the_dba_and_formation_date_when_the_cells_are_blank()
    {
        var customer = ExistingBusiness(dba: "Old Name", formationDate: new DateOnly(2010, 1, 1));

        Apply(customer, BusinessRow(structure: "Llc", dba: null, formationDate: null), DuplicateStrategy.Overwrite);

        Assert.Equal("Old Name", customer.BusinessIdentity!.Dba);
        Assert.Equal(new DateOnly(2010, 1, 1), customer.BusinessIdentity.FormationDate);
    }

    [Fact]
    public void Merge_fills_only_the_missing_business_data()
    {
        var customer = ExistingBusiness(dba: "Old Name", formationDate: null);

        var result = Apply(
            customer,
            BusinessRow(structure: "SCorp", dba: "New Name", formationDate: "2015-03-01"),
            DuplicateStrategy.Merge
        );

        Assert.True(result.Value);
        Assert.Equal("Old Name", customer.BusinessIdentity!.Dba);
        Assert.Equal(BusinessStructure.Llc, customer.BusinessIdentity.Structure);
        Assert.Equal(new DateOnly(2015, 3, 1), customer.BusinessIdentity.FormationDate);
    }

    [Fact]
    public void Overwrite_updates_the_date_of_birth_and_the_name_parts_in_the_file()
    {
        var customer = ExistingIndividual(prefix: "Dr", dateOfBirth: null);

        var result = Apply(
            customer,
            IndividualRow(middleName: "King", dateOfBirth: "1990-01-02"),
            DuplicateStrategy.Overwrite
        );

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        Assert.Equal("King", customer.PersonalName!.MiddleName);
        Assert.Equal("Dr", customer.PersonalName.Prefix);
        Assert.Equal(new DateOnly(1990, 1, 2), customer.DateOfBirth);
    }

    [Fact]
    public void Merge_fills_a_missing_date_of_birth_but_never_replaces_one()
    {
        var empty = ExistingIndividual(dateOfBirth: null);
        var filled = ExistingIndividual(dateOfBirth: new DateOnly(1980, 5, 5));

        Apply(empty, IndividualRow(dateOfBirth: "1990-01-02"), DuplicateStrategy.Merge);
        Apply(filled, IndividualRow(dateOfBirth: "1990-01-02"), DuplicateStrategy.Merge);

        Assert.Equal(new DateOnly(1990, 1, 2), empty.DateOfBirth);
        Assert.Equal(new DateOnly(1980, 5, 5), filled.DateOfBirth);
    }

    [Fact]
    public void Overwrite_replaces_the_primary_home_address()
    {
        var customer = ExistingIndividual();
        customer.AddAddress(AddressKind.Home, Address("1 Old St"), isPrimary: true, Actor);

        Apply(customer, IndividualRow(addressLine1: "2 New Ave"), DuplicateStrategy.Overwrite);

        var home = Assert.Single(customer.Addresses);
        Assert.Equal("2 New Ave", home.Address.Line1);
        Assert.True(home.IsPrimary);
    }

    [Fact]
    public void Merge_adds_the_address_only_when_the_client_has_none()
    {
        var withoutAddress = ExistingIndividual();
        var withAddress = ExistingIndividual();
        withAddress.AddAddress(AddressKind.Home, Address("1 Old St"), isPrimary: true, Actor);

        Apply(withoutAddress, IndividualRow(addressLine1: "2 New Ave"), DuplicateStrategy.Merge);
        Apply(withAddress, IndividualRow(addressLine1: "2 New Ave"), DuplicateStrategy.Merge);

        Assert.Equal("2 New Ave", Assert.Single(withoutAddress.Addresses).Address.Line1);
        Assert.Equal("1 Old St", Assert.Single(withAddress.Addresses).Address.Line1);
    }

    [Fact]
    public void A_different_client_type_keeps_the_identity_and_warns()
    {
        var customer = ExistingIndividual();
        var warnings = new List<string>();

        var result = Apply(
            customer,
            BusinessRow(structure: "Llc", dba: "Ada Co", formationDate: null),
            DuplicateStrategy.Overwrite,
            warnings
        );

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        Assert.Equal(CustomerKind.Individual, customer.Kind);
        Assert.Equal("Ada Lovelace", customer.DisplayName);
        Assert.Contains(warnings, w => w.Contains("Business", StringComparison.Ordinal));
    }

    [Fact]
    public void Merge_with_nothing_new_reports_no_changes_and_does_not_touch_the_client()
    {
        var customer = ExistingIndividual(dateOfBirth: new DateOnly(1980, 5, 5));
        var touchedAt = customer.UpdatedAtUtc;

        var result = Apply(customer, IndividualRow(dateOfBirth: "1990-01-02"), DuplicateStrategy.Merge);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        Assert.False(result.Value);
        Assert.Equal(touchedAt, customer.UpdatedAtUtc);
    }

    [Fact]
    public void Overwrite_with_the_same_values_reports_no_changes()
    {
        var customer = ExistingBusiness(dba: "Acme Roofs", formationDate: new DateOnly(2015, 3, 1));
        var touchedAt = customer.UpdatedAtUtc;

        var result = Apply(
            customer,
            BusinessRow(structure: "Llc", dba: "Acme Roofs", formationDate: "2015-03-01"),
            DuplicateStrategy.Overwrite
        );

        Assert.False(result.Value);
        Assert.Equal(touchedAt, customer.UpdatedAtUtc);
    }

    private static BuildingBlocks.Results.Result<bool> Apply(
        DomainCustomer customer,
        ParsedRow row,
        DuplicateStrategy strategy,
        List<string>? warnings = null
    ) =>
        ImportRowApplier.ApplyUpdate(
            customer,
            row,
            occupationId: null,
            businessIdentity: row.BusinessIdentity,
            Actor,
            strategy,
            blindIndex: null,
            new NoProtector(),
            warnings
        );

    private static ParsedRow IndividualRow(
        string? middleName = null,
        string? dateOfBirth = null,
        string? addressLine1 = null
    ) =>
        ImportRowParser
            .Parse(
                new ImportCustomerRow
                {
                    RowNumber = 2,
                    Kind = "Individual",
                    FirstName = "Ada",
                    MiddleName = middleName,
                    LastName = "Lovelace",
                    Email = "ada@example.com",
                    DateOfBirth = dateOfBirth,
                    AddressLine1 = addressLine1,
                    City = addressLine1 is null ? null : "Miami",
                    Region = addressLine1 is null ? null : "FL",
                    PostalCode = addressLine1 is null ? null : "33101",
                    CountryCode = addressLine1 is null ? null : "US",
                }
            )
            .Value;

    private static ParsedRow BusinessRow(string structure, string? dba, string? formationDate) =>
        ImportRowParser
            .Parse(
                new ImportCustomerRow
                {
                    RowNumber = 2,
                    Kind = "Business",
                    LegalName = "Acme Roofing LLC",
                    BusinessStructure = structure,
                    Dba = dba,
                    FormationDate = formationDate,
                    Email = "office@acme.example.com",
                }
            )
            .Value;

    private static DomainCustomer ExistingIndividual(string? prefix = null, DateOnly? dateOfBirth = null) =>
        DomainCustomer
            .Register(
                Guid.NewGuid(),
                CustomerKind.Individual,
                PersonalName.Create("Ada", "Lovelace", prefix: prefix).Value,
                null,
                EmailAddress.Create("ada@example.com").Value,
                null,
                Language.En,
                Actor,
                dateOfBirth: dateOfBirth
            )
            .Value;

    private static DomainCustomer ExistingBusiness(string? dba, DateOnly? formationDate) =>
        DomainCustomer
            .Register(
                Guid.NewGuid(),
                CustomerKind.Business,
                null,
                BusinessIdentity.Create("Acme Roofing LLC", BusinessStructure.Llc, dba, formationDate).Value,
                EmailAddress.Create("office@acme.example.com").Value,
                null,
                Language.En,
                Actor
            )
            .Value;

    private static AddressValue Address(string line1) =>
        AddressValue.Create(line1, "Miami", "33101", "US", region: "FL").Value;

    private sealed class NoProtector : ISensitiveDataProtector
    {
        public short CurrentKeyVersion => 1;

        public byte[] Protect(string plainText) => [1, 2, 3];

        public string Unprotect(byte[] cipher) => string.Empty;

        public string ComputeBlindIndex(string plainText, Guid tenantId) => $"blind-{plainText}";

        public IReadOnlyCollection<string> ComputeBlindIndexCandidates(string plainText, Guid tenantId) => [];
    }
}
