using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Customer.Application.Abstractions;
using TaxVision.Customer.Application.Customers;
using TaxVision.Customer.Domain.Customers;
using TaxVision.Customer.Domain.Customers.ValueObjects;
using TaxVision.Customer.Infrastructure.Persistence;
using DomainCustomer = TaxVision.Customer.Domain.Customers.Customer;

namespace TaxVision.Customer.Tests.Application;

/// <summary>Conteos por estado del overview del dashboard, con el mismo recorte por asignación que el total.</summary>
public sealed class CustomerOverviewStatusCountsTests
{
    private static readonly Guid Admin = Guid.NewGuid();

    [Fact]
    public async Task GetOverviewAsync_counts_by_status_within_the_assignment_scope()
    {
        var databaseName = Guid.NewGuid().ToString();
        var tenantId = Guid.NewGuid();
        var employee = Guid.NewGuid();
        await using (var seed = CreateContext(databaseName, tenantId))
        {
            for (var i = 0; i < 2; i++)
                seed.Customers.Add(NewCustomer(tenantId, _ => Result.Success()));
            seed.Customers.Add(NewCustomer(tenantId, c => c.Deactivate(Admin)));
            seed.Customers.Add(NewCustomer(tenantId, c => c.Archive(Admin)));
            seed.Customers.Add(NewCustomer(tenantId, c => c.AssignPreparer(employee, Admin)));
            seed.Customers.Add(
                NewCustomer(
                    tenantId,
                    c =>
                    {
                        c.AssignPreparer(employee, Admin);
                        return c.Deactivate(Admin);
                    }
                )
            );
            // Otra oficina: no cuenta.
            seed.Customers.Add(NewCustomer(Guid.NewGuid(), _ => Result.Success()));
            await seed.SaveChangesAsync();
        }

        await using var db = CreateContext(databaseName, tenantId);
        var reader = new CustomerReadService(db, new PlainProtector());

        var office = await reader.GetOverviewAsync(tenantId, months: 12);
        var mine = await reader.GetOverviewAsync(tenantId, months: 12, assignedToUserId: employee);

        Assert.Equal(new CustomerStatusCounts(3, 2, 1), office.StatusCounts);
        Assert.Equal(new CustomerStatusCounts(1, 1, 0), mine.StatusCounts);
        Assert.Equal(6, office.TotalCount);
        Assert.Equal(2, mine.TotalCount);
    }

    [Fact]
    public async Task GetOverviewAsync_for_empty_tenant_returns_zero_status_counts()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateContext(Guid.NewGuid().ToString(), tenantId);

        var overview = await new CustomerReadService(db, new PlainProtector()).GetOverviewAsync(tenantId, 12);

        Assert.Equal(new CustomerStatusCounts(0, 0, 0), overview.StatusCounts);
        Assert.Equal(0, overview.TotalCount);
    }

    [Fact]
    public void The_customers_table_has_a_tenant_created_index()
    {
        using var db = new CustomerDbContext(
            new DbContextOptionsBuilder<CustomerDbContext>()
                .UseSqlServer("Server=model-only;Database=model-only")
                .Options,
            new FixedTenant(Guid.Empty)
        );

        var index = db
            .Model.FindEntityType(typeof(DomainCustomer))!
            .GetIndexes()
            .SingleOrDefault(i => i.GetDatabaseName() == "IX_Customers_TenantId_CreatedAtUtc");

        Assert.NotNull(index);
        Assert.Equal(
            [nameof(DomainCustomer.TenantId), nameof(DomainCustomer.CreatedAtUtc)],
            index.Properties.Select(p => p.Name)
        );
    }

    private static CustomerDbContext CreateContext(string databaseName, Guid tenantId) =>
        new(
            new DbContextOptionsBuilder<CustomerDbContext>().UseInMemoryDatabase(databaseName).Options,
            new FixedTenant(tenantId)
        );

    private static DomainCustomer NewCustomer(Guid tenantId, Func<DomainCustomer, Result> arrange)
    {
        var customer = DomainCustomer
            .Register(
                tenantId,
                CustomerKind.Individual,
                PersonalName.Create("Ada", "Client").Value,
                null,
                EmailAddress.Create($"overview-{Guid.NewGuid():N}@example.test").Value,
                null,
                Language.En,
                Admin
            )
            .Value;
        Assert.True(arrange(customer).IsSuccess);
        return customer;
    }

    private sealed class FixedTenant(Guid tenantId) : ITenantContext
    {
        public Guid TenantId => tenantId;
        public bool HasTenant => true;

        public void SetTenant(Guid tenantId) { }
    }

    private sealed class PlainProtector : ISensitiveDataProtector
    {
        public byte[] Protect(string plainText) => System.Text.Encoding.UTF8.GetBytes(plainText);

        public string Unprotect(byte[] cipher) => System.Text.Encoding.UTF8.GetString(cipher);

        public string ComputeBlindIndex(string plainText, Guid tenantId) => plainText;

        public IReadOnlyCollection<string> ComputeBlindIndexCandidates(string plainText, Guid tenantId) => [plainText];

        public short CurrentKeyVersion => 1;
    }
}
