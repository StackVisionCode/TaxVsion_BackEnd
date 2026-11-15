using TaxVision.Customer.Application.Abstractions;
using TaxVision.Customer.Application.Imports.Dtos;
using TaxVision.Customer.Application.Imports.Helpers;
using TaxVision.Customer.Application.Imports.Messages;
using TaxVision.Customer.Domain.Customers;
using TaxVision.Customer.Domain.Customers.ValueObjects;
using TaxVision.Customer.Domain.Imports;
using DomainCustomer = TaxVision.Customer.Domain.Customers.Customer;

namespace TaxVision.Customer.Tests.Application;

public sealed class RunCustomerImportHandlerTests
{
    // P20 (TZ15-H7): el año fiscal del cónyuge importado es el año en curso de la oficina, no el UTC.
    [Fact]
    public void Default_tax_year_is_the_office_local_year()
    {
        // 03:00Z del 1-ene-2027 = 21:00 del 31-dic-2026 en Chicago.
        var newYearUtc = new DateTime(2027, 1, 1, 3, 0, 0, DateTimeKind.Utc);

        Assert.Equal(
            2026,
            ImportRowApplier.DefaultTaxYear(
                newYearUtc,
                BuildingBlocks.TimeZones.ResolvedTimeZone.Resolve("America/Chicago")
            )
        );
        Assert.Equal(
            2027,
            ImportRowApplier.DefaultTaxYear(newYearUtc, BuildingBlocks.TimeZones.ResolvedTimeZone.Resolve(null))
        );
    }

    // El detector ya excluye archivados: esto cubre al que se archiva entre el cruce y la fila.
    [Fact]
    public void ApplyUpdate_ArchivedCustomer_ReturnsFailureInsteadOfReportingTheRowAsUpdated()
    {
        var actor = Guid.NewGuid();
        var customer = DomainCustomer
            .Register(
                Guid.NewGuid(),
                CustomerKind.Individual,
                PersonalName.Create("Ada", "Lovelace").Value,
                null,
                EmailAddress.Create("ada@example.com").Value,
                null,
                Language.En,
                actor
            )
            .Value;
        customer.Archive(actor);
        var row = ImportRowParser
            .Parse(
                new ImportCustomerRow
                {
                    RowNumber = 2,
                    Kind = "Individual",
                    FirstName = "Ada",
                    LastName = "Lovelace",
                    Email = "ada.new@example.com",
                    TaxIdentifier = "123-45-6789",
                }
            )
            .Value;

        var result = ImportRowApplier.ApplyUpdate(
            customer,
            row,
            occupationId: null,
            businessIdentity: null,
            actor,
            DuplicateStrategy.Overwrite,
            blindIndex: null,
            protector: null!
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Customer.Archived", result.Error.Code);
        Assert.Equal("ada@example.com", customer.PrimaryEmail.Value);
    }
}
