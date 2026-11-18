using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Customer.Domain.Customers;
using TaxVision.Customer.Domain.Customers.ValueObjects;
using TaxVision.Customer.Infrastructure.Persistence;
using TaxVision.Customer.Infrastructure.Persistence.Repositories;
using Xunit;
using DomainCustomer = TaxVision.Customer.Domain.Customers.Customer;

namespace TaxVision.Customer.Tests.Integration;

/// <summary>
/// D-3 en la base: varios clientes sin email conviven en la misma oficina (el único de email ignora los vacíos),
/// se leen con el email nulo y el email agregado después se guarda. Dos clientes con el mismo email siguen chocando.
/// Base temporal con las migraciones reales: el único de email está escrito a mano en SQL.
/// </summary>
public sealed class CustomerOptionalEmailIntegrationTests
{
    private static readonly Guid Actor = Guid.NewGuid();

    private readonly Guid _tenant = Guid.NewGuid();

    [Fact]
    public async Task Two_clients_without_email_live_in_the_same_office()
    {
        var first = WithoutEmail("3055550001");
        var second = WithoutEmail("3055550002");

        await using (var db = NewContext())
        {
            db.Customers.AddRange(first, second);
            await db.SaveChangesAsync();
        }

        await using var read = NewContext();
        var loaded = await new CustomerRepository(read).GetByIdAsync(_tenant, second.Id, CancellationToken.None);
        Assert.Null(loaded!.PrimaryEmail);
        Assert.Equal("+13055550002", loaded.PrimaryPhone!.E164Value);
    }

    [Fact]
    public async Task An_email_added_later_is_saved()
    {
        var customer = WithoutEmail("3055550003");
        await using (var db = NewContext())
        {
            db.Customers.Add(customer);
            await db.SaveChangesAsync();
        }

        await using (var db = NewContext())
        {
            var loaded = await new CustomerRepository(db).GetByIdAsync(_tenant, customer.Id, CancellationToken.None);
            loaded!.ChangePrimaryEmail(EmailAddress.Create("late@example.test").Value, Actor);
            await db.SaveChangesAsync();
        }

        await using var read = NewContext();
        var saved = await new CustomerRepository(read).GetByIdAsync(_tenant, customer.Id, CancellationToken.None);
        Assert.Equal("late@example.test", saved!.PrimaryEmail!.NormalizedValue);
    }

    [Fact]
    public async Task The_same_email_twice_in_one_office_still_collides()
    {
        var email = $"same-{Guid.NewGuid():N}@example.test";
        await using var db = NewContext();
        db.Customers.AddRange(WithEmail(email), WithEmail(email));

        await Assert.ThrowsAsync<ConflictException>(() => db.SaveChangesAsync());
    }

    private DomainCustomer WithoutEmail(string phone) =>
        DomainCustomer
            .Register(
                _tenant,
                CustomerKind.Individual,
                PersonalName.Create("No", "Email").Value,
                null,
                null,
                PhoneNumber.Create($"+1{phone}").Value,
                Language.En,
                Actor
            )
            .Value;

    private DomainCustomer WithEmail(string email) =>
        DomainCustomer
            .Register(
                _tenant,
                CustomerKind.Individual,
                PersonalName.Create("Same", "Email").Value,
                null,
                EmailAddress.Create(email).Value,
                null,
                Language.En,
                Actor
            )
            .Value;

    private CustomerDbContext NewContext() =>
        new(
            new DbContextOptionsBuilder<CustomerDbContext>()
                .UseSqlServer(CustomerIntegrationDatabase.ConnectionString)
                .Options,
            new FixedTenant(_tenant)
        );

    private sealed class FixedTenant(Guid tenantId) : ITenantContext
    {
        public Guid TenantId => tenantId;
        public bool HasTenant => true;

        public void SetTenant(Guid id) { }
    }
}
