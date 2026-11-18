using Microsoft.EntityFrameworkCore;
using TaxVision.Customer.Domain.Customers;
using TaxVision.Customer.Domain.Customers.ValueObjects;
using TaxVision.Customer.Domain.FiscalProfiles;
using TaxVision.Customer.Infrastructure.Persistence.Repositories;
using Xunit;
using DomainCustomer = TaxVision.Customer.Domain.Customers.Customer;

namespace TaxVision.Customer.Tests.Integration;

/// <summary>
/// D-4 en la base: varios clientes de la misma oficina pueden tener el SSN pendiente (el índice único ignora los
/// vacíos) y el perfil se lee igual, sin identificador.
/// </summary>
public sealed class CustomerPendingTaxIdentifierIntegrationTests(CustomerTemporaryDatabase database)
    : IClassFixture<CustomerTemporaryDatabase>
{
    private static readonly Guid Actor = Guid.NewGuid();

    private readonly Guid _tenant = Guid.NewGuid();

    [Fact]
    public async Task Two_clients_of_the_same_office_can_have_the_tax_id_pending()
    {
        var first = NewCustomer();
        var second = NewCustomer();

        await using (var db = database.NewContext(_tenant))
        {
            db.Customers.AddRange(first, second);
            await db.SaveChangesAsync();
        }

        await using var read = database.NewContext(_tenant);
        var loaded = await new CustomerRepository(read).GetByIdAsync(_tenant, second.Id, CancellationToken.None);
        Assert.False(loaded!.FiscalProfile!.HasTaxIdentifier);
        Assert.Equal(FilingStatus.MarriedJoint, loaded.FiscalProfile.FilingStatus);
        Assert.Equal(
            2,
            await read.CustomerFiscalProfiles.IgnoreQueryFilters().CountAsync(fp => fp.TenantId == _tenant)
        );
    }

    private DomainCustomer NewCustomer()
    {
        var customer = DomainCustomer
            .Register(
                _tenant,
                CustomerKind.Individual,
                PersonalName.Create("Pending", "Ssn").Value,
                null,
                EmailAddress.Create($"pending-{Guid.NewGuid():N}@example.test").Value,
                null,
                Language.En,
                Actor
            )
            .Value;
        customer.SetFiscalProfileWithoutTaxIdentifier(
            FiscalSubjectKind.Individual,
            FilingStatus.MarriedJoint,
            80_000m,
            false,
            Actor
        );
        return customer;
    }
}
