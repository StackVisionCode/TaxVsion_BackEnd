using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using TaxVision.Customer.Domain.Customers;
using TaxVision.Customer.Domain.Customers.ValueObjects;
using TaxVision.Customer.Infrastructure.Persistence.Repositories;
using Xunit;
using DomainCustomer = TaxVision.Customer.Domain.Customers.Customer;

namespace TaxVision.Customer.Tests.Integration;

/// <summary>CUS-5, en una base temporal: el tenant filtra en la consulta y no queda a cargo de cada llamador.</summary>
public sealed class CustomerRepositoryIntegrationTests(CustomerTemporaryDatabase database)
    : IClassFixture<CustomerTemporaryDatabase>
{
    private static readonly Guid Actor = Guid.NewGuid();

    private readonly Guid _tenant = Guid.NewGuid();

    [Fact]
    public async Task GetById_OtherTenant_ReturnsNull()
    {
        var id = await SeedAsync();
        await using var db = database.NewContext(Guid.NewGuid());

        var found = await new CustomerRepository(db).GetByIdAsync(Guid.NewGuid(), id, CancellationToken.None);

        Assert.Null(found);
    }

    [Fact]
    public async Task GetById_OwnTenant_ReturnsTheCustomer()
    {
        var id = await SeedAsync();
        await using var db = database.NewContext(_tenant);

        var found = await new CustomerRepository(db).GetByIdAsync(_tenant, id, CancellationToken.None);

        Assert.Equal(id, found!.Id);
    }

    // Cuatro colecciones en un solo SELECT multiplican las filas entre sí.
    [Fact]
    public async Task GetById_LoadsTheCollectionsInSeparateQueries()
    {
        var id = await SeedAsync();
        var commands = new CountingCommands();
        await using var db = database.NewContext(_tenant, commands);

        await new CustomerRepository(db).GetByIdAsync(_tenant, id, CancellationToken.None);

        Assert.True(commands.Reads >= 5, $"Expected one query per collection, got {commands.Reads}.");
    }

    private async Task<Guid> SeedAsync()
    {
        var customer = DomainCustomer
            .Register(
                _tenant,
                CustomerKind.Individual,
                PersonalName.Create("Repo", "Client").Value,
                null,
                EmailAddress.Create($"cus5-{Guid.NewGuid():N}@example.test").Value,
                null,
                Language.En,
                Actor
            )
            .Value;
        await using var db = database.NewContext(_tenant);
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        return customer.Id;
    }

    private sealed class CountingCommands : DbCommandInterceptor
    {
        public int Reads { get; private set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default
        )
        {
            Reads++;
            return ValueTask.FromResult(result);
        }
    }
}
