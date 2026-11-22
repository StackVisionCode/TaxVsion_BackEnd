using BuildingBlocks.TimeZones;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TaxVision.Billing.Application.Abstractions;
using TaxVision.Billing.Application.Invoices.GetInvoicesDashboardSummary;
using TaxVision.Billing.Application.Invoices.ListInvoices;
using TaxVision.Billing.Domain.Invoices;
using TaxVision.Billing.Domain.ValueObjects;
using TaxVision.Billing.Infrastructure.Persistence;
using TaxVision.Billing.Infrastructure.Persistence.Repositories;
using Xunit;
using static TaxVision.Billing.Tests.Integration.Performance.DashboardPerformance;

namespace TaxVision.Billing.Tests.Integration.Performance;

/// <summary>
/// Volumen del dashboard en Billing: 50 000 facturas y 100 000 pagos en un tenant de prueba, más tres tenants de
/// ruido con un tercio cada uno, en una base temporal propia. Mide el resumen y el listado open/past-due con la
/// oficina completa y con un empleado de 500 clientes asignados de 20 000. Borra lo sembrado al terminar.
/// </summary>
[Trait("Category", "Performance")]
public sealed class BillingDashboardPerformanceTests : IAsyncLifetime
{
    private const int Invoices = 50_000;
    private const int Customers = 20_000;
    private const int Employees = 40;
    private static readonly DateTime Now = new(2026, 11, 21, 15, 0, 0, DateTimeKind.Utc);
    private static readonly ResolvedTimeZone Zone = ResolvedTimeZone.Resolve("America/New_York");
    private static readonly Guid[] Tenants = [.. AllTenants];
    private static readonly Guid Employee = Id("a5e5", 1, 0);

    private static readonly string[] OwnTables =
    [
        "[billing].[InvoicePayments]",
        "[billing].[Invoices]",
        "[billing].[CustomerAssignmentProjections]",
    ];

    private readonly BillingTestDatabase _database = new("billing_perf_it");

    public async Task InitializeAsync()
    {
        await _database.InitializeAsync();
        var template = await TemplateAsync();
        for (var t = 1; t <= Tenants.Length; t++)
            await SeedTenantAsync(t, template, DivisorOf(t));
        await UpdateStatisticsAsync(_database.ConnectionString, [.. OwnTables, "[billing].[InvoiceLineItems]"]);
    }

    public async Task DisposeAsync()
    {
        try
        {
            var cs = _database.ConnectionString;
            foreach (var child in new[] { "InvoiceLineItems", "InvoicePaymentLinks", "InvoiceStatusChanges" })
                await DeleteChildrenAsync(cs, $"[billing].[{child}]", "InvoiceId", "[billing].[Invoices]", Tenants);
            await DeleteAsync(cs, Tenants, OwnTables);
            Assert.All((await CountAsync(cs, Tenants, OwnTables)).Values, count => Assert.Equal(0, count));
            Assert.All(
                (await CountAsync(BillingTestDatabase.LocalConnectionString, Tenants, OwnTables)).Values,
                count => Assert.Equal(0, count)
            );
        }
        finally
        {
            await _database.DisposeAsync();
        }
    }

    [Fact]
    public async Task Dashboard_summary_and_open_lists_with_volume()
    {
        Assert.Equal(
            Invoices + 1,
            (await CountAsync(_database.ConnectionString, [Tenants[0]], OwnTables[1]))[OwnTables[1]]
        );

        foreach (var (scope, canViewAll) in new[] { ("oficina", true), ("empleado 500/20 000", false) })
        {
            await MeasureAsync(
                "Billing",
                "GET billing/invoices/dashboard-summary",
                scope,
                _database.ConnectionString,
                async capture =>
                {
                    await using var db = NewContext(capture);
                    var result = await GetInvoicesDashboardSummaryHandler.Handle(
                        new GetInvoicesDashboardSummaryQuery(Tenants[0], Employee, canViewAll),
                        new InvoiceDashboardReader(db),
                        new FixedZone(),
                        new FixedClock(),
                        Visibility,
                        default
                    );
                    Assert.True(result.IsSuccess);
                }
            );

            foreach (var status in new[] { InvoiceListStatus.Open, InvoiceListStatus.PastDue })
                await MeasureAsync(
                    "Billing",
                    $"GET billing/invoices?status={(status == InvoiceListStatus.Open ? "open" : "past-due")}&take=50",
                    scope,
                    _database.ConnectionString,
                    async capture =>
                    {
                        await using var db = NewContext(capture);
                        var result = await ListInvoicesHandler.Handle(
                            new ListInvoicesQuery(Tenants[0], Employee, canViewAll, 50, null, status),
                            new InvoiceRepository(db),
                            Visibility,
                            new FixedZone(),
                            new FixedClock(),
                            default
                        );
                        Assert.True(result.IsSuccess);
                        Assert.NotEmpty(result.Value);
                    }
                );
        }
    }

    private static readonly IOptions<BillingVisibilityOptions> Visibility = Options.Create(
        new BillingVisibilityOptions { Enabled = true }
    );

    // Una factura real emitida con un pago y una línea: las copias heredan JSON, importes y estados válidos.
    private async Task<Guid> TemplateAsync()
    {
        var invoice = Invoice
            .CreateDraft(
                Tenants[0],
                Employee,
                new CustomerSnapshot(Id("c057", 1, 0), "Client", "client@example.com", null, null, null),
                "USD",
                [new DraftInvoiceLine("Tax preparation", 1, 10_000, 0)],
                null,
                Now.AddDays(-30)
            )
            .Value;
        Assert.True(
            invoice
                .Issue(InvoiceNumber.Create("INV-2026-99999").Value, Now.AddDays(-30), Now.AddDays(-10), Employee)
                .IsSuccess
        );
        Assert.True(
            invoice
                .ApplyPayment(
                    InvoicePaymentSource.Manual,
                    "tpl",
                    Money.Create(4_000, "USD").Value,
                    PaymentMethod.Cash,
                    Now.AddDays(-20),
                    Employee
                )
                .IsSuccess
        );
        await using var db = NewContext(null);
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync();
        return invoice.Id;
    }

    private async Task SeedTenantAsync(int t, Guid template, int divisor)
    {
        var cs = _database.ConnectionString;
        var tenant = $"'{Tenants[t - 1]}'";
        var invoiceId = SqlId("1a00", t, "n");
        var day = "(n % 400)";
        // Estados por n % 20: 1 borrador, 1 anulada, 4 emitidas, 2 enviadas, 2 parciales y 10 pagadas.
        var status =
            "CASE WHEN n % 20 = 0 THEN N'Draft' WHEN n % 20 = 1 THEN N'Voided' WHEN n % 20 < 6 THEN N'Issued' "
            + "WHEN n % 20 < 8 THEN N'Sent' WHEN n % 20 < 10 THEN N'PartiallyPaid' ELSE N'Paid' END";
        var total = "(10000 + (n % 50) * 100)";
        var paid = $"CASE WHEN n % 20 >= 10 THEN {total} WHEN n % 20 >= 8 THEN {total} / 2 ELSE 0 END";
        var issued = $"DATEADD(DAY, -{day}, CAST('2026-11-21T12:00:00' AS datetime2))";

        await CloneAsync(
            cs,
            "[billing].[Invoices]",
            $"[Id] = '{template}'",
            Invoices / divisor,
            new Dictionary<string, string>
            {
                ["Id"] = invoiceId,
                ["TenantId"] = tenant,
                ["CustomerId"] = SqlId("c057", t, $"n % {Customers}"),
                ["InvoiceNumber"] = "CONCAT(N'P-', n)",
                ["Status"] = status,
                ["Currency"] = "N'USD'",
                ["Total"] = $"CONCAT({total}, N'|USD')",
                ["Subtotal"] = $"CONCAT({total}, N'|USD')",
                ["AmountPaid"] = $"CONCAT({paid}, N'|USD')",
                ["AmountDue"] = $"CONCAT({total} - {paid}, N'|USD')",
                ["IssueDateUtc"] = issued,
                ["DueDateUtc"] = $"DATEADD(DAY, 30, {issued})",
                ["CreatedAtUtc"] = $"DATEADD(HOUR, -1, {issued})",
                ["UpdatedAtUtc"] = issued,
                ["DeletedAtUtc"] = "NULL",
            }
        );
        await CloneAsync(
            cs,
            "[billing].[InvoiceLineItems]",
            $"[InvoiceId] = '{template}'",
            Invoices / divisor,
            new Dictionary<string, string> { ["Id"] = "NEWID()", ["InvoiceId"] = invoiceId }
        );
        // Dos pagos por factura: 1 de cada 50 es crédito arrastrado y 1 de cada 40 está reversado.
        await CloneAsync(
            cs,
            "[billing].[InvoicePayments]",
            $"[InvoiceId] = '{template}'",
            2 * (Invoices / divisor),
            new Dictionary<string, string>
            {
                ["Id"] = "NEWID()",
                ["TenantId"] = tenant,
                ["InvoiceId"] = SqlId("1a00", t, "n / 2"),
                ["Amount"] = "N'5000|USD'",
                ["PaidAtUtc"] =
                    $"DATEADD(DAY, 3 + (n % 2) * 7 - ((n / 2) % 400), CAST('2026-11-21T12:00:00' AS datetime2))",
                ["Source"] = "CASE WHEN n % 50 = 0 THEN N'CarriedCredit' ELSE N'Manual' END",
                ["SourceReference"] = "CONCAT(N'perf-', n)",
                ["ReversedAtUtc"] = "CASE WHEN n % 40 = 1 THEN CAST('2026-11-01' AS datetime2) END",
            }
        );
        await InsertAsync(
            cs,
            "[billing].[CustomerAssignmentProjections]",
            Customers / divisor,
            new Dictionary<string, string>
            {
                ["Id"] = "NEWID()",
                ["TenantId"] = tenant,
                ["CustomerId"] = SqlId("c057", t, "n"),
                ["UserId"] = SqlId("a5e5", t, $"(n / 50) % {Employees}"),
                ["Version"] = "SYSUTCDATETIME()",
            }
        );
    }

    private BillingDbContext NewContext(CommandCapture? capture)
    {
        var options = new DbContextOptionsBuilder<BillingDbContext>().UseSqlServer(_database.ConnectionString);
        if (capture is not null)
            options.AddInterceptors(capture);
        return new BillingDbContext(options.Options, new NoTenant());
    }

    private sealed class FixedZone : IOfficeTimeZone
    {
        public Task<ResolvedTimeZone> ResolveAsync(Guid tenantId, CancellationToken ct) => Task.FromResult(Zone);
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class NoTenant : BuildingBlocks.Tenancy.ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid id) { }
    }
}
