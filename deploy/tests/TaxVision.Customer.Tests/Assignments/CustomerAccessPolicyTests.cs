using System.Text.RegularExpressions;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Customer.Application.Customers;
using TaxVision.Customer.Application.Customers.Commands.RemoveRelation;
using TaxVision.Customer.Domain.Customers;
using TaxVision.Customer.Domain.Customers.ValueObjects;
using TaxVision.Customer.Infrastructure.Persistence;
using TaxVision.Customer.Infrastructure.Persistence.Repositories;
using DomainCustomer = TaxVision.Customer.Domain.Customers.Customer;

namespace TaxVision.Customer.Tests.Assignments;

/// <summary>
/// A1 — la visibilidad por asignación ya filtraba la lectura, pero las mutaciones entraban con solo
/// tener <c>customers.manage</c>. Un cliente que no le tocó al empleado se comporta como inexistente:
/// un 403 confirmaría que ese id existe y sobre quién está trabajando un colega.
/// </summary>
public sealed class CustomerAccessPolicyTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    private sealed class FakeTenantContext : ITenantContext
    {
        private Guid? _tenantId;
        public Guid TenantId => _tenantId ?? throw new InvalidOperationException("TenantId is not set.");
        public bool HasTenant => _tenantId.HasValue;

        public void SetTenant(Guid tenantId) => _tenantId = tenantId;
    }

    private static CustomerDbContext CreateContext(string databaseName, FakeTenantContext tenantContext) =>
        new(new DbContextOptionsBuilder<CustomerDbContext>().UseInMemoryDatabase(databaseName).Options, tenantContext);

    private static DomainCustomer NewCustomer() =>
        DomainCustomer
            .Register(
                Tenant,
                CustomerKind.Individual,
                PersonalName.Create("Ada", "Lovelace").Value,
                null,
                EmailAddress.Create($"ada-{Guid.NewGuid():N}@example.com").Value,
                null,
                Language.En,
                PreferredChannel.Email,
                Guid.NewGuid()
            )
            .Value;

    // ---------- la regla ----------

    [Fact]
    public void An_assigned_user_can_access_the_customer()
    {
        var customer = NewCustomer();
        var assignee = Guid.NewGuid();
        customer.AssignPreparer(assignee, Guid.NewGuid());

        Assert.True(CustomerAccessPolicy.CanAccess(customer, assignee, canViewAllCustomers: false));
    }

    [Fact]
    public void An_unassigned_user_cannot_access_the_customer()
    {
        var customer = NewCustomer();
        customer.AssignPreparer(Guid.NewGuid(), Guid.NewGuid());

        Assert.False(CustomerAccessPolicy.CanAccess(customer, Guid.NewGuid(), canViewAllCustomers: false));
    }

    [Fact]
    public void View_all_is_the_office_wide_bypass()
    {
        var customer = NewCustomer();

        Assert.True(CustomerAccessPolicy.CanAccess(customer, Guid.NewGuid(), canViewAllCustomers: true));
    }

    // ---------- el guard, contra un handler real ----------

    private static async Task<Guid> SeedAssignedCustomerAsync(string databaseName, Guid assignee)
    {
        var tenantContext = new FakeTenantContext();
        tenantContext.SetTenant(Tenant);
        await using var db = CreateContext(databaseName, tenantContext);
        var customer = NewCustomer();
        customer.AssignPreparer(assignee, Guid.NewGuid());
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        return customer.Id;
    }

    private static async Task<string> RemoveRelationAsAsync(
        string databaseName,
        Guid customerId,
        Guid caller,
        bool canViewAll
    )
    {
        var tenantContext = new FakeTenantContext();
        tenantContext.SetTenant(Tenant);
        await using var db = CreateContext(databaseName, tenantContext);
        var result = await RemoveRelationHandler.Handle(
            new RemoveRelationCommand(Tenant, customerId, Guid.NewGuid(), caller, canViewAll),
            new CustomerRepository(db),
            db,
            CancellationToken.None
        );
        return result.IsSuccess ? "ok" : result.Error.Code;
    }

    [Fact]
    public async Task A_customer_of_another_preparer_reads_as_not_found()
    {
        var databaseName = Guid.NewGuid().ToString();
        var customerId = await SeedAssignedCustomerAsync(databaseName, Guid.NewGuid());

        var code = await RemoveRelationAsAsync(databaseName, customerId, Guid.NewGuid(), canViewAll: false);

        Assert.Equal("Customer.NotFound", code);
    }

    [Fact]
    public async Task The_assigned_preparer_gets_past_the_guard()
    {
        var databaseName = Guid.NewGuid().ToString();
        var assignee = Guid.NewGuid();
        var customerId = await SeedAssignedCustomerAsync(databaseName, assignee);

        // La relación no existe, así que falla más adelante — lo que importa es que ya no es NotFound
        // del cliente: el guard lo dejó pasar.
        var code = await RemoveRelationAsAsync(databaseName, customerId, assignee, canViewAll: false);

        Assert.NotEqual("Customer.NotFound", code);
    }

    [Fact]
    public async Task An_admin_with_view_all_gets_past_the_guard()
    {
        var databaseName = Guid.NewGuid().ToString();
        var customerId = await SeedAssignedCustomerAsync(databaseName, Guid.NewGuid());

        var code = await RemoveRelationAsAsync(databaseName, customerId, Guid.NewGuid(), canViewAll: true);

        Assert.NotEqual("Customer.NotFound", code);
    }

    // ---------- que ningún handler de mutación se quede fuera ----------

    [Fact]
    public void Every_customer_mutation_handler_applies_the_policy()
    {
        string[] commands =
        [
            "Update",
            "AddAddress",
            "UpdateAddress",
            "RemoveAddress",
            "AddContactPoint",
            "UpdateContactPoint",
            "RemoveContactPoint",
            "AddRelation",
            "UpdateRelation",
            "RemoveRelation",
            "RevealTaxIdentifier",
        ];

        var root = RepositoryRoot();
        var missing = commands
            .Where(command =>
            {
                var directory = Path.Combine(
                    root,
                    "src",
                    "Services",
                    "Customer",
                    "TaxVision.Customer.Application",
                    "Customers",
                    "Commands",
                    command
                );
                var handler = Directory.GetFiles(directory, "*Handler.cs").Single();
                return !File.ReadAllText(handler).Contains("CustomerAccessPolicy", StringComparison.Ordinal);
            })
            .ToList();

        Assert.True(missing.Count == 0, "Sin chequeo de asignación: " + string.Join(", ", missing));
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TaxVision.slnx")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        return directory.FullName;
    }
}
