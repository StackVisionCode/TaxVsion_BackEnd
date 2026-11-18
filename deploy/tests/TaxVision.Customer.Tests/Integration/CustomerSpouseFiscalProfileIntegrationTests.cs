using Microsoft.EntityFrameworkCore;
using TaxVision.Customer.Domain.Customers;
using TaxVision.Customer.Domain.Customers.ValueObjects;
using TaxVision.Customer.Domain.Relations;
using TaxVision.Customer.Infrastructure.Persistence.Repositories;
using Xunit;
using DomainCustomer = TaxVision.Customer.Domain.Customers.Customer;

namespace TaxVision.Customer.Tests.Integration;

/// <summary>
/// El perfil fiscal del cónyuge se guarda de verdad: al crear el cliente, al agregarle el cónyuge a un cliente
/// existente y al reemplazar su SSN (una sola fila por cónyuge). Sin Id preasignado.
/// </summary>
public sealed class CustomerSpouseFiscalProfileIntegrationTests(CustomerTemporaryDatabase database)
    : IClassFixture<CustomerTemporaryDatabase>
{
    private static readonly Guid Actor = Guid.NewGuid();

    private readonly Guid _tenant = Guid.NewGuid();

    [Fact]
    public async Task A_new_client_is_saved_with_its_spouse_fiscal_profile()
    {
        var customer = NewCustomer();
        AttachSpouse(customer, "4321");

        await using (var db = database.NewContext(_tenant))
        {
            db.Customers.Add(customer);
            await db.SaveChangesAsync();
        }

        Assert.Equal(["4321"], await SpouseLast4Async(customer.Id));
    }

    [Fact]
    public async Task An_existing_client_gets_a_spouse_with_its_fiscal_profile()
    {
        var id = await SeedAsync(withSpouse: false);

        await using (var db = database.NewContext(_tenant))
        {
            var existing = await new CustomerRepository(db).GetByIdAsync(_tenant, id, CancellationToken.None);
            AttachSpouse(existing!, "5555");
            await db.SaveChangesAsync();
        }

        Assert.Equal(["5555"], await SpouseLast4Async(id));
    }

    [Fact]
    public async Task Replacing_the_spouse_tax_id_keeps_a_single_profile()
    {
        var id = await SeedAsync(withSpouse: true);

        await using (var db = database.NewContext(_tenant))
        {
            var existing = await new CustomerRepository(db).GetByIdAsync(_tenant, id, CancellationToken.None);
            var spouse = existing!.Relations.Single(r => r.RelationshipKind == RelationshipKind.Spouse);
            var result = existing.SetRelationFiscalProfile(
                spouse.Id,
                TaxRelationshipRole.Spouse,
                [9, 9, 9],
                $"blind-{Guid.NewGuid():N}",
                "1111",
                2026,
                qualifiesAsDependent: false,
                livedWithTaxpayer: true,
                Actor
            );
            Assert.True(result.IsSuccess);
            await db.SaveChangesAsync();
        }

        Assert.Equal(["1111"], await SpouseLast4Async(id));
    }

    private DomainCustomer NewCustomer() =>
        DomainCustomer
            .Register(
                _tenant,
                CustomerKind.Individual,
                PersonalName.Create("Spouse", "Owner").Value,
                null,
                EmailAddress.Create($"spouse-{Guid.NewGuid():N}@example.test").Value,
                null,
                Language.En,
                Actor
            )
            .Value;

    private static void AttachSpouse(DomainCustomer customer, string last4)
    {
        var spouse = customer
            .AddRelation(
                RelationshipKind.Spouse,
                RelationPurpose.TaxHouseholdMember,
                PersonalName.Create("Sam", "Owner").Value,
                null,
                null,
                null,
                null,
                Actor
            )
            .Value;
        var result = spouse.SetFiscalProfile(
            TaxRelationshipRole.Spouse,
            [1, 2, 3],
            $"blind-{Guid.NewGuid():N}",
            last4,
            2026,
            qualifiesAsDependent: false,
            livedWithTaxpayer: true,
            Actor
        );
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
    }

    private async Task<Guid> SeedAsync(bool withSpouse)
    {
        var customer = NewCustomer();
        if (withSpouse)
            AttachSpouse(customer, "4321");
        await using var db = database.NewContext(_tenant);
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        return customer.Id;
    }

    private async Task<List<string>> SpouseLast4Async(Guid customerId)
    {
        await using var db = database.NewContext(_tenant);
        var relationIds = await db
            .CustomerRelations.IgnoreQueryFilters()
            .Where(r => r.CustomerId == customerId && r.RelationshipKind == RelationshipKind.Spouse)
            .Select(r => r.Id)
            .ToListAsync();
        return await db
            .CustomerRelationFiscalProfiles.IgnoreQueryFilters()
            .Where(fp => relationIds.Contains(fp.CustomerRelationId))
            .Select(fp => fp.TaxIdentifierLast4)
            .ToListAsync();
    }
}
