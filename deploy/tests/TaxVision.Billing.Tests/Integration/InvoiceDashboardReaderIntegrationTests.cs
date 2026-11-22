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
/// Totales del dashboard contra el SQL Server real: las consultas se traducen a SQL y dan lo mismo que el cálculo en
/// memoria sobre las mismas facturas. Cubre pago parcial en su mes, pago reversado, crédito arrastrado, pago de una
/// anulada, reemitida facturada una vez, otra moneda, aging por vencimiento y recorte por asignación.
/// </summary>
[Collection(BillingTestDatabase.Collection)]
public sealed class InvoiceDashboardReaderIntegrationTests(BillingTestDatabase database) : IAsyncLifetime
{
    private static readonly Guid Actor = Guid.NewGuid();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _assignedCustomer = Guid.NewGuid();
    private readonly Guid _otherCustomer = Guid.NewGuid();
    private readonly Guid _assignedUser = Guid.NewGuid();
    private readonly List<Invoice> _invoices = [];
    private int _number;

    private static readonly DateTime[] MonthBounds =
    [
        Utc(2026, 9, 1),
        Utc(2026, 10, 1),
        Utc(2026, 11, 1),
        Utc(2026, 12, 1),
    ];

    private static readonly DateTime[] InvoicedBounds =
    [
        Utc(2026, 6, 1),
        Utc(2026, 9, 1),
        Utc(2026, 10, 1),
        Utc(2026, 11, 1),
        Utc(2026, 12, 1),
    ];

    // "Hoy" = 21 de noviembre de 2026 en UTC.
    private static readonly InvoiceAgingCutoffs Cutoffs = new(
        Utc(2026, 11, 21),
        Utc(2026, 10, 22),
        Utc(2026, 9, 22),
        Utc(2026, 8, 23)
    );

    public async Task InitializeAsync()
    {
        // Parcial: 4 000 en octubre y 3 000 en noviembre; vence el 4 de noviembre.
        var partial = Issued(_assignedCustomer, 10_000, Utc(2026, 10, 5), Utc(2026, 11, 4));
        Pay(partial, 4_000, Utc(2026, 10, 20));
        Pay(partial, 3_000, Utc(2026, 11, 10));

        // Cobrada y corregida a "sin cobrar": el pago queda reversado y vuelve a deber todo.
        var corrected = Issued(_otherCustomer, 5_000, Utc(2026, 10, 1), Utc(2026, 10, 31));
        Pay(corrected, 5_000, Utc(2026, 10, 15));
        Assert.True(
            corrected.OverrideStatus(InvoiceStatus.Issued, "Wrong payment", Utc(2026, 10, 16), Actor).IsSuccess
        );

        // Reemitida: la original anulada conserva su cobro; el reemplazo recibe crédito arrastrado.
        var original = Issued(_otherCustomer, 8_000, Utc(2026, 9, 10), Utc(2026, 10, 10));
        Pay(original, 8_000, Utc(2026, 9, 12));
        var replacement = original.CreateReplacementDraft(Actor, Utc(2026, 11, 2)).Value;
        Assert.True(original.ReissueAs(replacement, null, Utc(2026, 11, 2), Actor).IsSuccess);
        IssueAs(replacement, Utc(2026, 11, 2), Utc(2026, 12, 2));
        Assert.True(replacement.ApplyCarriedCredit(Utc(2026, 11, 2)).IsSuccess);
        _invoices.Add(replacement);

        // Otra moneda: fuera de todas las sumas.
        var pesos = Issued(_otherCustomer, 50_000, Utc(2026, 11, 5), Utc(2026, 12, 5), "DOP");
        Pay(pesos, 1_000, Utc(2026, 11, 6));

        // Borrador: ni facturado ni pendiente.
        _invoices.Add(Draft(_otherCustomer, 2_000, Utc(2026, 11, 7)));

        // Muy vieja (más de 90 días vencida) y una que todavía no vence (la más reciente: manda su moneda).
        Issued(_otherCustomer, 9_000, Utc(2026, 6, 1), Utc(2026, 7, 1));
        Issued(_otherCustomer, 1_000, Utc(2026, 11, 20), Utc(2026, 12, 20));

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

    [Fact]
    public async Task Computed_cents_match_the_stored_amounts_and_payments_carry_the_tenant()
    {
        await using var db = NewContext();

        var invoices = await db
            .Invoices.IgnoreQueryFilters()
            .Where(i => i.TenantId == _tenantId)
            .Select(i => new
            {
                i.Id,
                Total = EF.Property<long>(i, "TotalCents"),
                Paid = EF.Property<long>(i, "AmountPaidCents"),
                Due = EF.Property<long>(i, "AmountDueCents"),
            })
            .ToListAsync();
        var ids = _invoices.Select(i => i.Id).ToList();
        var payments = await db.Set<InvoicePayment>()
            .Where(p => ids.Contains(p.InvoiceId))
            .Select(p => new
            {
                p.Id,
                p.TenantId,
                Cents = EF.Property<long>(p, "AmountCents"),
            })
            .ToListAsync();

        foreach (var invoice in _invoices)
        {
            var row = invoices.Single(r => r.Id == invoice.Id);
            Assert.Equal(invoice.Total.AmountCents, row.Total);
            Assert.Equal(invoice.AmountPaid.AmountCents, row.Paid);
            Assert.Equal(invoice.AmountDue.AmountCents, row.Due);
        }
        var expectedPayments = _invoices.SelectMany(i => i.Payments).ToList();
        Assert.Equal(expectedPayments.Count, payments.Count);
        foreach (var payment in expectedPayments)
        {
            var row = payments.Single(r => r.Id == payment.Id);
            Assert.Equal(payment.Amount.AmountCents, row.Cents);
            Assert.Equal(_tenantId, row.TenantId);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Collected_per_range_matches_the_in_memory_payments(bool restricted)
    {
        await using var db = NewContext();
        var reader = new InvoiceDashboardReader(db);

        var sums = await reader.CollectedByRangeAsync(Scope(restricted), MonthBounds);

        Assert.Equal(ExpectedCollected(restricted, MonthBounds), sums);
        // Septiembre: la anulada; octubre: el parcial (el reversado no); noviembre: el parcial (ni crédito ni DOP).
        Assert.Equal(restricted ? new long[] { 0, 4_000, 3_000 } : new long[] { 8_000, 4_000, 3_000 }, sums);
    }

    [Fact]
    public async Task Invoiced_per_range_counts_a_reissued_invoice_once()
    {
        await using var db = NewContext();
        var reader = new InvoiceDashboardReader(db);

        var sums = await reader.InvoicedByRangeAsync(Scope(restricted: false), InvoicedBounds);

        var expected = Ranges(InvoicedBounds)
            .Select(range =>
                Visible(restricted: false)
                    .Where(i => i.Status is not (InvoiceStatus.Draft or InvoiceStatus.Voided))
                    .Where(i => i.IssueDateUtc >= range.From && i.IssueDateUtc < range.To)
                    .Sum(i => i.Total.AmountCents)
            )
            .ToList();
        Assert.Equal(expected, sums);
        Assert.Equal(new long[] { 9_000, 0, 15_000, 8_000 + 1_000 }, sums);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Aging_buckets_follow_the_due_date(bool restricted)
    {
        await using var db = NewContext();
        var reader = new InvoiceDashboardReader(db);

        var aging = await reader.AgingAsync(Scope(restricted), Cutoffs);

        var open = Open(restricted).ToList();
        Assert.Equal(open.Sum(i => i.AmountDue.AmountCents), aging.OutstandingCents);
        Assert.Equal(open.Count, aging.OpenCount);
        Assert.Equal(open.Count(i => i.DueDateUtc < Cutoffs.PastDueBeforeUtc), aging.PastDueCount);
        Assert.Equal(open.Where(i => i.DueDateUtc >= Cutoffs.Over30BeforeUtc).Sum(Due), aging.UpTo30Cents);
        Assert.Equal(open.Where(i => i.DueDateUtc < Cutoffs.Over90BeforeUtc).Sum(Due), aging.Over90Cents);
        if (restricted)
            Assert.Equal(new InvoiceAgingTotals(3_000, 1, 1, 3_000, 1, 0, 0, 0, 0, 0, 0), aging);
        else
            Assert.Equal(new InvoiceAgingTotals(18_000, 4, 3, 9_000, 3, 0, 0, 0, 0, 9_000, 1), aging);
    }

    [Fact]
    public async Task Oldest_open_invoices_come_by_due_date()
    {
        await using var db = NewContext();
        var reader = new InvoiceDashboardReader(db);

        var oldest = await reader.OldestOpenAsync(Scope(restricted: false), 3);

        Assert.Equal(new long[] { 9_000, 5_000, 3_000 }, oldest.Select(o => o.DueCents));
        Assert.Equal(new[] { Utc(2026, 7, 1), Utc(2026, 10, 31), Utc(2026, 11, 4) }, oldest.Select(o => o.BaseDateUtc));
        Assert.All(oldest, o => Assert.NotNull(o.InvoiceNumber));
    }

    [Fact]
    public async Task The_currency_is_the_one_of_the_latest_visible_invoice()
    {
        await using var db = NewContext();
        var reader = new InvoiceDashboardReader(db);

        Assert.Equal("USD", await reader.LatestCurrencyAsync(_tenantId, null));
        Assert.Equal("USD", await reader.LatestCurrencyAsync(_tenantId, _assignedUser));
        Assert.Null(await reader.LatestCurrencyAsync(_tenantId, Guid.NewGuid()));
    }

    private IReadOnlyList<long> ExpectedCollected(bool restricted, IReadOnlyList<DateTime> bounds)
    {
        var payments = Visible(restricted)
            .SelectMany(i => i.Payments)
            .Where(p => p.IsActive && !p.IsCarriedCredit)
            .ToList();
        return Ranges(bounds)
            .Select(range =>
                payments.Where(p => p.PaidAtUtc >= range.From && p.PaidAtUtc < range.To).Sum(p => p.Amount.AmountCents)
            )
            .ToList();
    }

    private IEnumerable<Invoice> Visible(bool restricted) =>
        _invoices.Where(i =>
            i.Currency == "USD" && i.DeletedAtUtc is null && (!restricted || i.CustomerId == _assignedCustomer)
        );

    private IEnumerable<Invoice> Open(bool restricted) =>
        Visible(restricted)
            .Where(i => i.Status is InvoiceStatus.Issued or InvoiceStatus.Sent or InvoiceStatus.PartiallyPaid)
            .Where(i => i.AmountDue.AmountCents > 0);

    private static long Due(Invoice invoice) => invoice.AmountDue.AmountCents;

    private static IEnumerable<(DateTime From, DateTime To)> Ranges(IReadOnlyList<DateTime> bounds) =>
        bounds.Zip(bounds.Skip(1));

    private InvoiceDashboardScope Scope(bool restricted) => new(_tenantId, restricted ? _assignedUser : null, "USD");

    private Invoice Draft(Guid customerId, long totalCents, DateTime createdUtc, string currency = "USD") =>
        Invoice
            .CreateDraft(
                _tenantId,
                Actor,
                new CustomerSnapshot(customerId, "Client", "client@example.com", null, null, null),
                currency,
                [new DraftInvoiceLine("Tax preparation", 1, totalCents, 0)],
                null,
                createdUtc
            )
            .Value;

    private Invoice Issued(
        Guid customerId,
        long totalCents,
        DateTime issuedUtc,
        DateTime dueUtc,
        string currency = "USD"
    )
    {
        var invoice = Draft(customerId, totalCents, issuedUtc, currency);
        IssueAs(invoice, issuedUtc, dueUtc);
        _invoices.Add(invoice);
        return invoice;
    }

    private void IssueAs(Invoice invoice, DateTime issuedUtc, DateTime dueUtc) =>
        Assert.True(
            invoice.Issue(InvoiceNumber.Create($"INV-2026-{++_number:D5}").Value, issuedUtc, dueUtc, Actor).IsSuccess
        );

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
