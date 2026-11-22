using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using TaxVision.Customer.Application.Abstractions;
using TaxVision.Customer.Application.Customers.Queries.Overview;
using TaxVision.Customer.Domain.Customers;
using TaxVision.Customer.Domain.Customers.ValueObjects;
using TaxVision.Customer.Infrastructure.Persistence;
using static TaxVision.Customer.Tests.Integration.Performance.DashboardPerformance;
using DomainCustomer = TaxVision.Customer.Domain.Customers.Customer;

namespace TaxVision.Customer.Tests.Integration.Performance;

/// <summary>
/// Volumen del dashboard en Customer: 20 000 clientes con su asignación en un tenant de prueba y los de ruido, en la
/// base temporal BB-10. Mide <c>GET /customers/overview</c> (statusCounts, altas por mes y últimos 3) con la oficina
/// completa y con un empleado de 500 clientes asignados de 20 000.
/// </summary>
[Trait("Category", "Performance")]
public sealed class CustomerDashboardPerformanceTests : IAsyncLifetime
{
    private const int Customers = 20_000;
    private const int Employees = 40;
    private static readonly DateTime Now = new(2026, 11, 21, 15, 0, 0, DateTimeKind.Utc);
    private static readonly Guid[] Tenants = [.. AllTenants];
    private static readonly Guid Employee = Id("a5e5", 1, 0);
    private static readonly string ConnectionString = CustomerIntegrationDatabase.ConnectionString;
    private static readonly string[] OwnTables = ["[dbo].[CustomerAssignments]", "[dbo].[Customers]"];

    public async Task InitializeAsync()
    {
        var template = await TemplateAsync();
        for (var t = 1; t <= Tenants.Length; t++)
            await SeedTenantAsync(t, template, DivisorOf(t));
        await UpdateStatisticsAsync(ConnectionString, OwnTables);
    }

    public async Task DisposeAsync()
    {
        await DeleteAsync(ConnectionString, Tenants, OwnTables);
        Assert.All((await CountAsync(ConnectionString, Tenants, OwnTables)).Values, count => Assert.Equal(0, count));
        Assert.All(
            (await CountAsync(FleetConnectionString(), Tenants, OwnTables)).Values,
            count => Assert.Equal(0, count)
        );
    }

    [Fact]
    public async Task Overview_with_volume()
    {
        Assert.Equal(Customers, (await CountAsync(ConnectionString, [Tenants[0]], OwnTables[1]))[OwnTables[1]]);

        foreach (var (scope, canViewAll) in new[] { ("oficina", true), ("empleado 500/20 000", false) })
            await MeasureAsync(
                "Customer",
                "GET /customers/overview?months=6",
                scope,
                ConnectionString,
                async capture =>
                {
                    await using var db = NewContext(capture);
                    var overview = await CustomerOverviewHandler.Handle(
                        new CustomerOverviewQuery(Tenants[0], 6, Employee, canViewAll),
                        new CustomerReadService(db, null!, new FixedClock()),
                        Options.Create(new CustomerVisibilityOptions { Enabled = true }),
                        default
                    );
                    Assert.True(overview.TotalCount > 0);
                }
            );
    }

    // Un cliente real: las copias heredan columnas válidas. Es el n = 0 del tenant medido.
    private static async Task<Guid> TemplateAsync()
    {
        var customer = DomainCustomer
            .Register(
                Tenants[0],
                CustomerKind.Individual,
                PersonalName.Create("Volume", "Client").Value,
                null,
                EmailAddress.Create("template@example.test").Value,
                null,
                Language.En,
                Employee
            )
            .Value;
        await using var db = NewContext(null, Tenants[0]);
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        return customer.Id;
    }

    private static async Task SeedTenantAsync(int t, Guid template, int divisor)
    {
        var tenant = $"'{Tenants[t - 1]}'";
        // El primero reemplaza a la plantilla al final, para que el tenant medido tenga justo 20 000.
        var rows = t == 1 ? Customers / divisor - 1 : Customers / divisor;
        var n = t == 1 ? "(n + 1)" : "n";
        // Por n % 10: 1 archivado, 1 inactivo y 8 activos; un alta cada 20 minutos hacia atrás.
        await CloneAsync(
            ConnectionString,
            "[dbo].[Customers]",
            $"[Id] = '{template}'",
            rows,
            new Dictionary<string, string>
            {
                ["Id"] = SqlId("c057", t, n),
                ["TenantId"] = tenant,
                ["Status"] =
                    $"CASE WHEN {n} % 10 = 0 THEN N'Archived' WHEN {n} % 10 = 1 THEN N'Inactive' ELSE N'Active' END",
                ["DisplayName"] = $"CONCAT(N'Client ', {n})",
                ["PrimaryEmail"] = $"CONCAT(N'c', {n}, N'@example.test')",
                ["PrimaryEmailNormalized"] = $"CONCAT(N'c', {n}, N'@example.test')",
                ["AssignedPreparerUserId"] = SqlId("a5e5", t, $"({n} / 50) % {Employees}"),
                ["CreatedAtUtc"] = $"DATEADD(MINUTE, -20 * {n}, CAST('2026-11-21T14:00:00' AS datetime2))",
            }
        );
        await InsertAsync(
            ConnectionString,
            "[dbo].[CustomerAssignments]",
            Customers / divisor,
            new Dictionary<string, string>
            {
                ["Id"] = "NEWID()",
                ["TenantId"] = tenant,
                ["CustomerId"] =
                    t == 1
                        ? $"CASE WHEN n = 0 THEN '{template}' ELSE {SqlId("c057", t, "n")} END"
                        : SqlId("c057", t, "n"),
                ["UserId"] = SqlId("a5e5", t, $"(n / 50) % {Employees}"),
            }
        );
    }

    private static string FleetConnectionString() =>
        Environment.GetEnvironmentVariable("CUSTOMER_DB_CONNECTION")
        ?? new ConfigurationBuilder()
            .AddUserSecrets("8c4e7963-1ddb-441e-af2d-755d58fb86a3")
            .Build()
            .GetConnectionString("Default")!;

    private static CustomerDbContext NewContext(CommandCapture? capture, Guid tenant = default)
    {
        var options = new DbContextOptionsBuilder<CustomerDbContext>().UseSqlServer(ConnectionString);
        if (capture is not null)
            options.AddInterceptors(capture);
        return new CustomerDbContext(options.Options, new FixedTenant(tenant));
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class FixedTenant(Guid tenant) : ITenantContext
    {
        public Guid TenantId => tenant;
        public bool HasTenant => tenant != Guid.Empty;

        public void SetTenant(Guid id) { }
    }
}
