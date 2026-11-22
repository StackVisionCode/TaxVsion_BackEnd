using BuildingBlocks.CustomerVisibility;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Billing.Application.Abstractions;
using TaxVision.Billing.Domain.Invoices;
using TaxVision.Billing.Domain.ValueObjects;
using TaxVision.Billing.Infrastructure.Persistence;
using TaxVision.Billing.Infrastructure.Persistence.Repositories;
using Xunit;

namespace TaxVision.Billing.Tests.Integration;

/// <summary>
/// Filtros "open" y "past-due" del listado contra SQL Server real: el total de la lista es el OpenCount y el
/// PastDueCount del aging del dashboard, con el mismo recorte por asignación.
/// </summary>
[Collection(BillingTestDatabase.Collection)]
public sealed class InvoiceListFiltersIntegrationTests(BillingTestDatabase database) : IAsyncLifetime
{
    private static readonly Guid Actor = Guid.NewGuid();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _assignedCustomer = Guid.NewGuid();
    private readonly Guid _otherCustomer = Guid.NewGuid();
    private readonly Guid _assignedUser = Guid.NewGuid();
    private readonly List<Invoice> _invoices = [];
    private int _number;

    // "Hoy" = 21 de noviembre de 2026 en UTC.
    private static readonly InvoiceAgingCutoffs Cutoffs = new(
        Utc(2026, 11, 21),
        Utc(2026, 10, 22),
        Utc(2026, 9, 22),
        Utc(2026, 8, 23)
    );

    public async Task InitializeAsync()
    {
        // Pendientes: dos vencidas (una con pago parcial) y dos que todavía no vencen.
        var partial = Issued(_assignedCustomer, 10_000, Utc(2026, 10, 5), Utc(2026, 11, 4));
        Pay(partial, 4_000, Utc(2026, 10, 20));
        Issued(_otherCustomer, 5_000, Utc(2026, 10, 1), Utc(2026, 10, 31));
        Issued(_otherCustomer, 1_000, Utc(2026, 11, 20), Utc(2026, 12, 20));
        Issued(_assignedCustomer, 2_000, Utc(2026, 11, 15), Utc(2026, 12, 15));

        // Fuera: pagada, anulada vencida y borrador.
        var paid = Issued(_otherCustomer, 3_000, Utc(2026, 9, 1), Utc(2026, 10, 1));
        Pay(paid, 3_000, Utc(2026, 9, 15));
        var voided = Issued(_assignedCustomer, 7_000, Utc(2026, 9, 1), Utc(2026, 10, 1));
        Assert.True(voided.Void("Duplicate", Utc(2026, 9, 2), Actor).IsSuccess);
        _invoices.Add(Draft(_otherCustomer, 2_000, Utc(2026, 11, 7)));

        await using var db = NewContext();
        db.Invoices.AddRange(_invoices);
        db.CustomerAssignmentProjections.Add(
            CustomerAssignmentProjection.Create(_tenantId, _assignedCustomer, _assignedUser, DateTime.UtcNow)
        );
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await using var db = NewContext();
        await db.Invoices.IgnoreQueryFilters().Where(i => i.TenantId == _tenantId).ExecuteDeleteAsync();
        await db
            .CustomerAssignmentProjections.IgnoreQueryFilters()
            .Where(a => a.TenantId == _tenantId)
            .ExecuteDeleteAsync();
    }

    [Theory]
    [InlineData(false, 4, 2)]
    [InlineData(true, 2, 1)]
    public async Task Open_and_past_due_totals_match_the_dashboard_aging(bool restricted, int open, int pastDue)
    {
        await using var db = NewContext();
        var aging = await new InvoiceDashboardReader(db).AgingAsync(
            new InvoiceDashboardScope(_tenantId, AssignedTo(restricted), "USD"),
            Cutoffs
        );

        var openList = await ListAsync(restricted, new OpenInvoicesFilter());
        var pastDueList = await ListAsync(restricted, new OpenInvoicesFilter(Cutoffs.PastDueBeforeUtc));

        Assert.Equal((open, pastDue), (aging.OpenCount, aging.PastDueCount));
        Assert.Equal(aging.OpenCount, openList.Count);
        Assert.Equal(aging.PastDueCount, pastDueList.Count);
        Assert.Equal(aging.OutstandingCents, openList.Sum(i => i.AmountDue.AmountCents));
    }

    [Fact]
    public async Task Without_the_filter_the_list_is_unchanged()
    {
        var all = await ListAsync(restricted: false, open: null);

        Assert.Equal(_invoices.Count, all.Count);
    }

    private async Task<IReadOnlyList<Invoice>> ListAsync(bool restricted, OpenInvoicesFilter? open)
    {
        await using var db = NewContext();
        return await new InvoiceRepository(db).ListByTenantAsync(
            _tenantId,
            500,
            assignedToUserId: AssignedTo(restricted),
            open: open
        );
    }

    private Guid? AssignedTo(bool restricted) => restricted ? _assignedUser : null;

    private Invoice Draft(Guid customerId, long totalCents, DateTime createdUtc) =>
        Invoice
            .CreateDraft(
                _tenantId,
                Actor,
                new CustomerSnapshot(customerId, "Client", "client@example.com", null, null, null),
                "USD",
                [new DraftInvoiceLine("Tax preparation", 1, totalCents, 0)],
                null,
                createdUtc
            )
            .Value;

    private Invoice Issued(Guid customerId, long totalCents, DateTime issuedUtc, DateTime dueUtc)
    {
        var invoice = Draft(customerId, totalCents, issuedUtc);
        Assert.True(
            invoice.Issue(InvoiceNumber.Create($"INV-2026-{++_number:D5}").Value, issuedUtc, dueUtc, Actor).IsSuccess
        );
        _invoices.Add(invoice);
        return invoice;
    }

    private static void Pay(Invoice invoice, long cents, DateTime paidUtc) =>
        Assert.True(
            invoice
                .ApplyPayment(
                    InvoicePaymentSource.Manual,
                    Guid.NewGuid().ToString(),
                    Money.Create(cents, invoice.Currency).Value,
                    PaymentMethod.Cash,
                    paidUtc,
                    Actor
                )
                .IsSuccess
        );

    private BillingDbContext NewContext() =>
        new(
            new DbContextOptionsBuilder<BillingDbContext>().UseSqlServer(database.ConnectionString).Options,
            new FixedTenant(_tenantId)
        );

    private static DateTime Utc(int year, int month, int day) => new(year, month, day, 0, 0, 0, DateTimeKind.Utc);

    private sealed class FixedTenant(Guid tenantId) : ITenantContext
    {
        public Guid TenantId => tenantId;
        public bool HasTenant => true;

        public void SetTenant(Guid id) { }
    }
}
