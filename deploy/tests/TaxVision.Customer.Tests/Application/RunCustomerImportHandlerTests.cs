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
